'use strict';

// ============================================================
// Main — wires everything together and starts the game loop
// ============================================================
(function () {
  // Instantiate core systems
  const engine = new PriceEngine({ price: 100 });
  const candleBuilder = new CandleBuilder();
  const eventSystem = new EventSystem();
  const gameLoop = new GameLoop(engine, candleBuilder, eventSystem);
  const chartCanvas = document.getElementById('chart-canvas');
  const renderer = new ChartRenderer(chartCanvas);
  const ui = new UIManager(gameLoop, engine, renderer, candleBuilder);

  // Wire engine → candleBuilder via onCandleTick
  // (GameLoop.tick already calls engine.tick + candleBuilder.push,
  //  but the engine's direct tick (from no-op ticks) also needs to feed candles)
  engine.onCandleTick = null; // candle push handled in GameLoop.tick

  // Wire events
  gameLoop.onStateChange = (prev, next) => {
    const labels = {
      PRE_LAUNCH: '预热期',
      WORKDAY: '交易日',
      SETTLEMENT: '结算中',
      WEEKEND: '休息日',
      GAME_OVER: '游戏结束',
      VICTORY: '胜利',
    };
    eventSystem.pushSystem(`→ 进入${labels[next] || next}`);

    if (next === 'SETTLEMENT') {
      const pnl = gameLoop.weeklyPnl;
      const sign = pnl >= 0 ? '+' : '';
      eventSystem.pushSystem(`W${gameLoop.weekNumber} 周收益: ${sign}${pnl.toFixed(2)}`);
    }
  };

  gameLoop.onGameOver = (reason) => {
    const snap = engine.getSnapshot();
    const reasonText = {
      regulatory_liquidation: '监管清算：市场被强制关闭',
      market_collapse: '市场崩盘：价格归零，散户血本无归',
    }[reason] || reason;

    ui.showOverlay(
      '☠ 游戏结束',
      `<p>${reasonText}</p>
       <p>最终价格: $${snap.price.toFixed(2)}</p>
       <p>收割进度: ${(snap.harvestRatio * 100).toFixed(1)}%</p>
       <p>受众规模: ${Math.floor(snap.audience).toLocaleString()}</p>`,
      '重新开始',
      () => location.reload()
    );
  };

  gameLoop.onVictory = (snap) => {
    ui.showOverlay(
      '🏆 成功套现',
      `<p>你成功将集体幻觉兑换成了真实财富。</p>
       <p>最终收割: ${(snap.harvestRatio * 100).toFixed(1)}%</p>
       <p>最终价格: $${snap.price.toFixed(2)}</p>
       <p>美金入账: $${Math.floor(snap.poolHarvested).toLocaleString()}</p>`,
      '再来一局',
      () => location.reload()
    );
  };

  engine.onRegEvent = (type) => {
    const msgs = {
      warning: '⚠ 监管预警：风险已超过50%',
      investigation: '🔍 监管调查：市场信心下降',
      liquidation: '☠ 监管清算触发',
    };
    if (msgs[type]) eventSystem.pushSystem(msgs[type]);
  };

  // ============================================================
  // Tick interval
  // ============================================================
  let tickTimer = null;

  function startTick() {
    if (tickTimer) clearInterval(tickTimer);
    tickTimer = setInterval(() => {
      if (gameLoop.state === 'GAME_OVER' || gameLoop.state === 'VICTORY') return;
      gameLoop.tick();
    }, TIME_CONFIG.tickIntervalMs);
  }

  // Expose restart for dev panel speed slider
  window._restartTick = startTick;

  startTick();

  // ============================================================
  // Render loop (requestAnimationFrame)
  // ============================================================
  function renderLoop() {
    ui.renderChart();
    ui.updateAll();
    requestAnimationFrame(renderLoop);
  }

  // ============================================================
  // Canvas resize observer
  // ============================================================
  const resizeObs = new ResizeObserver(() => {
    renderer._resize();
  });
  resizeObs.observe(chartCanvas);

  // ============================================================
  // Start
  // ============================================================

  // Expose globals for inline scripts and dev access
  window._gameLoop = gameLoop;
  window._eventSystem = eventSystem;
  window._engine = engine;
  window._params = params;

  ui.init();
  requestAnimationFrame(renderLoop);

  // Push welcome message
  eventSystem.pushSystem('系统初始化完成 — 狗头徽章预热期开始');
})();
