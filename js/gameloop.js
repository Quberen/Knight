'use strict';

// ============================================================
// Operation catalog — AP cost only (USD is NOT consumed)
// ============================================================
const OPERATIONS = {
  INFO_POST: {
    id: 'INFO_POST', name: '发帖', nameEn: 'POST',
    ap: 1,
    input: { heatDelta: 0.01, emotionDelta: 0.05, regulatoryRiskAdd: 0.002 },
    desc: '发布利好内容，微推热度和情绪',
    availableInPreLaunch: true,
  },
  KOL_COLLAB: {
    id: 'KOL_COLLAB', name: 'KOL合作', nameEn: 'KOL',
    ap: 2,
    input: { heatDelta: 0.08, emotionDelta: 0.03, regulatoryRiskAdd: 0.01 },
    desc: '联合意见领袖，大幅推高热度',
    availableInPreLaunch: false,
  },
  COMMUNITY_EVENT: {
    id: 'COMMUNITY_EVENT', name: '社区活动', nameEn: 'COMMUNITY',
    ap: 2,
    input: { heatDelta: 0.06, audiencePurchased: 50, regulatoryRiskAdd: 0.008 },
    desc: '举办活动，拉新增加受众',
    availableInPreLaunch: true,
  },
  EXPERT_ENDORSE: {
    id: 'EXPERT_ENDORSE', name: '专家背书', nameEn: 'ENDORSE',
    ap: 2,
    input: { trustDelta: 0.05, heatDelta: 0.01, regulatoryRiskAdd: 0.005 },
    desc: '专家公开背书，提升信任度',
    availableInPreLaunch: true,
  },
  AUDIT_REPORT: {
    id: 'AUDIT_REPORT', name: '审计报告', nameEn: 'AUDIT',
    ap: 3,
    input: { trustDelta: 0.10, regulatoryRiskAdd: 0.003 },
    desc: '发布审计报告，大幅提升信任',
    availableInPreLaunch: true,
  },
  AD_PURCHASE: {
    id: 'AD_PURCHASE', name: '广告投放', nameEn: 'ADS',
    ap: 2,
    input: { audiencePurchased: 200, heatDelta: 0.03, regulatoryRiskAdd: 0.008 },
    desc: '买量投放，直接购买冷受众',
    availableInPreLaunch: false,
  },
  MARKET_SUPPORT: {
    id: 'MARKET_SUPPORT', name: '护盘', nameEn: 'SUPPORT',
    ap: 3,
    input: { momentumInject: 0.5, regulatoryRiskAdd: 0.03 },
    desc: '直接注入M，立即提振价格',
    availableInPreLaunch: false,
  },
};

// ============================================================
// Game Loop State Machine
// ============================================================
const GAME_STATE = {
  PRE_LAUNCH: 'PRE_LAUNCH',
  WORKDAY: 'WORKDAY',
  SETTLEMENT: 'SETTLEMENT',
  WEEKEND: 'WEEKEND',
  GAME_OVER: 'GAME_OVER',
  VICTORY: 'VICTORY',
};

class GameLoop {
  constructor(engine, candleBuilder, eventSystem) {
    this.engine = engine;
    this.candleBuilder = candleBuilder;
    this.eventSystem = eventSystem;

    // State
    this.state = GAME_STATE.PRE_LAUNCH;
    this.ticksInState = 0;

    // Time tracking
    this.totalTick = 0;
    this.weekNumber = 1;
    this.dayOfWeek = 1;  // 1-7
    this.dayTick = 0;    // tick within current day

    // State durations (in ticks)
    this.preLaunchDuration = 480;   // 2 days
    this.workdayDuration = 240;     // 1 day
    this.settlementDuration = 60;
    this.weekendDuration = 240;

    // Player resources
    this.usd = 0;           // earned via harvest only
    this.actionPoints = 5;
    this.maxActionPoints = 5;
    this.apResetPending = false;

    // Weekly tracking
    this.weekStartPrice = engine.price;
    this.weekEndPrice = engine.price;
    this.weeklyPnl = 0;
    this.weeklyOpsUsed = 0;

    // Direction (set on weekend)
    this.direction = 'NEUTRAL'; // 'LONG' | 'SHORT' | 'NEUTRAL'

    // Callbacks
    this.onStateChange = null;
    this.onGameOver = null;
    this.onVictory = null;
    this.onApReset = null;
    this.onOperationResult = null;

    // Wire engine game-over callback
    this.engine.onGameOver = (reason) => this._handleGameOver(reason);
  }

  // ============================================================
  // Advance one tick — called by main loop
  // ============================================================
  tick() {
    this.totalTick++;
    this.ticksInState++;
    this.dayTick++;

    // Day rollover
    if (this.dayTick >= TIME_CONFIG.ticksPerDay) {
      this.dayTick = 0;
      if (this.state === GAME_STATE.WORKDAY) {
        this._onDayEnd();
      }
    }

    // Process random event
    const ev = this.eventSystem.tick();
    if (ev && ev.effect) {
      this.engine.tick(ev.effect);
    } else {
      this.engine.tick(null);
    }

    // Feed candle builder
    const snap = this.engine.getSnapshot();
    this.candleBuilder.push(snap.price, snap);

    // Sync USD from harvest
    this.usd = Math.floor(snap.poolHarvested);

    // State transitions
    this._checkStateTransition(snap);

    // Win/loss check
    this._checkWinLoss(snap);
  }

  _onDayEnd() {
    // Reset AP at start of each workday
    this.actionPoints = this.maxActionPoints;
    if (this.onApReset) this.onApReset();
    this.dayOfWeek++;
    if (this.dayOfWeek > 5) {
      this.dayOfWeek = 1;
    }
  }

  _checkStateTransition(snap) {
    switch (this.state) {
      case GAME_STATE.PRE_LAUNCH:
        if (this.ticksInState >= this.preLaunchDuration) {
          this._transition(GAME_STATE.WORKDAY);
        }
        break;

      case GAME_STATE.WORKDAY:
        if (this.ticksInState >= this.workdayDuration * 5) {
          this.weekEndPrice = snap.price;
          this.weeklyPnl = this.weekEndPrice - this.weekStartPrice;
          this._transition(GAME_STATE.SETTLEMENT);
        }
        break;

      case GAME_STATE.SETTLEMENT:
        if (this.ticksInState >= this.settlementDuration) {
          this._transition(GAME_STATE.WEEKEND);
        }
        break;

      case GAME_STATE.WEEKEND:
        if (this.ticksInState >= this.weekendDuration) {
          this.weekNumber++;
          this.weekStartPrice = snap.price;
          this.dayOfWeek = 1;
          this.dayTick = 0;
          this.weeklyOpsUsed = 0;
          this.actionPoints = this.maxActionPoints;
          this._transition(GAME_STATE.WORKDAY);
        }
        break;
    }
  }

  _transition(newState) {
    const prev = this.state;
    this.state = newState;
    this.ticksInState = 0;
    if (this.onStateChange) this.onStateChange(prev, newState);
  }

  // ============================================================
  // Execute player operation
  // ============================================================
  executeOperation(opId) {
    const op = OPERATIONS[opId];
    if (!op) return { success: false, reason: 'unknown_op' };

    // State check
    if (this.state === GAME_STATE.PRE_LAUNCH && !op.availableInPreLaunch) {
      return { success: false, reason: 'not_available_prelaunch' };
    }
    if (this.state !== GAME_STATE.WORKDAY && this.state !== GAME_STATE.PRE_LAUNCH) {
      return { success: false, reason: 'wrong_state' };
    }
    if (this.state === GAME_STATE.GAME_OVER || this.state === GAME_STATE.VICTORY) {
      return { success: false, reason: 'game_ended' };
    }

    // AP check
    if (this.actionPoints < op.ap) {
      return { success: false, reason: 'insufficient_ap' };
    }

    // Deduct AP (USD is NOT consumed)
    this.actionPoints -= op.ap;
    this.weeklyOpsUsed++;

    // Send operation to engine
    const input = { ...op.input, type: op.id };
    this.engine.tick(input);

    // Feed candle builder
    const snap = this.engine.getSnapshot();
    this.candleBuilder.push(snap.price, snap);
    this.usd = Math.floor(snap.poolHarvested);

    if (this.onOperationResult) this.onOperationResult(op, snap);
    return { success: true, op, snap };
  }

  // ============================================================
  // Win / Loss
  // ============================================================
  _checkWinLoss(snap) {
    if (this.state === GAME_STATE.GAME_OVER || this.state === GAME_STATE.VICTORY) return;

    // Victory: harvested enough AND not in regulatory trouble
    if (snap.harvestRatio >= 0.7 && snap.regulatoryRisk < 0.8 && snap.marketConfidence > 0) {
      this._transition(GAME_STATE.VICTORY);
      if (this.onVictory) this.onVictory(snap);
      return;
    }

    // Loss: market confidence gone
    if (snap.marketConfidence <= 0) {
      this._handleGameOver('regulatory_liquidation');
      return;
    }

    // Loss: dead market with low harvest
    if (snap.price < 1 && snap.harvestRatio < 0.3) {
      this._handleGameOver('market_collapse');
    }
  }

  _handleGameOver(reason) {
    if (this.state === GAME_STATE.GAME_OVER || this.state === GAME_STATE.VICTORY) return;
    this._transition(GAME_STATE.GAME_OVER);
    if (this.onGameOver) this.onGameOver(reason);
  }

  // ============================================================
  // Getters for UI
  // ============================================================
  getAvailableOps() {
    return Object.values(OPERATIONS).filter(op => {
      if (this.state === GAME_STATE.PRE_LAUNCH) return op.availableInPreLaunch;
      if (this.state === GAME_STATE.WORKDAY) return true;
      return false;
    });
  }

  canExecute(opId) {
    const op = OPERATIONS[opId];
    if (!op) return false;
    if (this.state !== GAME_STATE.WORKDAY && this.state !== GAME_STATE.PRE_LAUNCH) return false;
    if (this.state === GAME_STATE.PRE_LAUNCH && !op.availableInPreLaunch) return false;
    return this.actionPoints >= op.ap;
  }

  getDayLabel() {
    if (this.state === GAME_STATE.PRE_LAUNCH) {
      const day = Math.floor(this.ticksInState / TIME_CONFIG.ticksPerDay) + 1;
      return `预热期 D${day}`;
    }
    if (this.state === GAME_STATE.WEEKEND) return `W${this.weekNumber} 休息日`;
    if (this.state === GAME_STATE.SETTLEMENT) return `W${this.weekNumber} 结算中`;
    return `W${this.weekNumber} D${this.dayOfWeek}`;
  }

  getStateProgressPct() {
    switch (this.state) {
      case GAME_STATE.PRE_LAUNCH:   return this.ticksInState / this.preLaunchDuration;
      case GAME_STATE.WORKDAY:      return this.ticksInState / (this.workdayDuration * 5);
      case GAME_STATE.SETTLEMENT:   return this.ticksInState / this.settlementDuration;
      case GAME_STATE.WEEKEND:      return this.ticksInState / this.weekendDuration;
      default: return 1;
    }
  }
}
