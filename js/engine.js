'use strict';

// Box-Muller transform for standard normal random variable
function boxMuller() {
  let u1, u2;
  do { u1 = Math.random(); } while (u1 === 0);
  u2 = Math.random();
  return Math.sqrt(-2 * Math.log(u1)) * Math.cos(2 * Math.PI * u2);
}

function clamp(v, lo, hi) {
  return Math.max(lo, Math.min(hi, v));
}

class PriceEngine {
  constructor(options = {}) {
    // Surface parameters (player-visible)
    this.trust = options.trust ?? 0.3;
    this.heat = options.heat ?? 0.1;
    this.surfaceEmotion = options.surfaceEmotion ?? 0.1;
    this.warmAudience = options.warmAudience ?? 100;
    this.coldAudience = options.coldAudience ?? 0;
    this.maxAudience = options.maxAudience ?? 10000;
    this.priceVelocity = 0;
    this.historicalHigh = options.price ?? 100;
    this.prevHeat = this.heat;

    // Core engine state (M/E/R)
    this.M = 0;
    this.E = 0;
    this.R = 0;

    // Active temporary overrides (from special ops)
    this.activeOverrides = [];

    // Perturbation layer (stepped OU, every k=5 ticks)
    this.perturbV = 0;
    this.perturbCounter = 0;
    this.perturbK = 5;

    // Price state
    this.price = options.price ?? 100;
    this.initPrice = this.price;
    this.prevPrice = this.price;

    // Pool / harvest
    this.poolTotal = options.poolTotal ?? 1000000;
    this.poolHarvested = 0;

    // Regulatory / confidence
    this.regulatoryRisk = 0;
    this.marketConfidence = 1.0;

    // Timestamps
    this.timestamp = 0;
    this.noiseAccumCounter = 0;

    // Price history (last 500 ticks)
    this.priceHistory = [this.price];

    // Callbacks
    this.onCandleTick = null; // called every tick with price
    this.onRegEvent = null;   // called on regulatory threshold events
    this.onGameOver = null;

    // Snapshot cache
    this._snapshot = null;
    this._lastMGen = { effectiveReach: 0, baseGen: 0, fomoGen: 0, delta: 0 };
    this._lastBullBear = { bullish: 0, bearish: 0 };
  }

  // ============================================================
  // Main tick — 12-step evolution (TDD v0.2 §3.3)
  // ============================================================
  tick(operation = null) {
    const p = params; // current ParamEnvelope

    // Step 1: params already read via `p`

    // Step 2: Process operation input
    if (operation) {
      // Surface param changes
      if (operation.heatDelta) this.heat = clamp(this.heat + operation.heatDelta, 0, 1);
      if (operation.trustDelta) this.trust = clamp(this.trust + operation.trustDelta, 0, 1);
      if (operation.emotionDelta) this.surfaceEmotion = clamp(this.surfaceEmotion + operation.emotionDelta, 0, 1);
      if (operation.audiencePurchased) this.coldAudience += operation.audiencePurchased;
      // Direct M injection (market support / rug ops)
      if (operation.momentumInject) this.M += operation.momentumInject;
      // Regulatory risk
      if (operation.regulatoryRiskAdd) {
        this.regulatoryRisk = clamp(this.regulatoryRisk + operation.regulatoryRiskAdd, 0, 1);
      }
      // Special override effects
      if (operation.gammaOverride != null) {
        this.activeOverrides.push({
          gammaOverride: operation.gammaOverride,
          trustDecayOverride: operation.trustDecayOverride ?? null,
          remaining: operation.overrideDuration ?? 30,
          postEffectTrustDelta: operation.postEffectTrustDelta ?? 0,
        });
      }
    }

    // Step 3: Process active overrides (countdown, apply expiry penalty)
    let currentGamma = p.gamma;
    for (let i = this.activeOverrides.length - 1; i >= 0; i--) {
      const ov = this.activeOverrides[i];
      if (ov.gammaOverride != null) currentGamma = ov.gammaOverride;
      ov.remaining--;
      if (ov.remaining <= 0) {
        if (ov.postEffectTrustDelta) {
          this.trust = clamp(this.trust + ov.postEffectTrustDelta, 0, 1);
        }
        this.activeOverrides.splice(i, 1);
      }
    }

    // Step 4: Natural decay
    this.heat = this.heat * (1 - p.heatDecay);
    this.surfaceEmotion = this.surfaceEmotion * (1 - p.emotionDecay);
    // trust: no natural decay — only ops/events

    // Step 5: Audience infection model
    const totalAudience = this.warmAudience + this.coldAudience;
    const S = Math.max(0, this.maxAudience - this.warmAudience - this.coldAudience);

    // 5a. Organic growth
    const spreadRate = p.baseSpread * this.heat * this.trust *
      (1 + this.priceVelocity * p.priceBoost);
    const organicGrowth = this.warmAudience * S * spreadRate;
    this.warmAudience += organicGrowth;

    // 5b. Cold → warm conversion
    const converted = this.coldAudience * p.warmRate * this.heat * this.trust;
    this.coldAudience -= converted;
    this.warmAudience += converted;

    // 5c. Churn
    const prevPriceForChurn = this.priceHistory.length > 1
      ? this.priceHistory[this.priceHistory.length - 2] : this.price;
    const priceDrop = Math.max(0, (prevPriceForChurn - this.price) / (prevPriceForChurn + 1e-9));
    const churnMultiplier = Math.max(1,
      1 + priceDrop * 5 + (this.trust < 0.2 ? 3 : 0) + (this.heat < 0.01 ? 2 : 0));
    const churn = totalAudience * p.churnRate * churnMultiplier;
    this.warmAudience = Math.max(0, this.warmAudience - churn * 0.7);
    this.coldAudience = Math.max(0, this.coldAudience - churn * 0.3);

    // Step 6: M generation layer
    const effectiveReach = (this.warmAudience + this.coldAudience * p.coldFactor) * this.heat;
    const deltaHeat = this.heat - this.prevHeat;
    const baseGen = effectiveReach * this.trust * this.surfaceEmotion * p.convRate;
    const fomoGen = Math.max(deltaHeat, 0) * effectiveReach * this.trust * p.fomoScale;
    const trustMultiplier = Math.pow(Math.max(this.trust, 1e-6), p.trustExponent);
    const mDelta = (baseGen + fomoGen) * trustMultiplier;
    this.M += mDelta;

    this._lastMGen = { effectiveReach, baseGen, fomoGen, delta: mDelta };
    this.prevHeat = this.heat;

    // Noise accumulator: inject tiny M every 30 ticks to keep market breathing
    this.noiseAccumCounter++;
    if (this.noiseAccumCounter >= 30) {
      this.M += 0.001;
      this.noiseAccumCounter = 0;
    }

    // Step 7: Core engine — Scheme C
    const eps = 1e-9;
    const regressPressure = clamp(this.R / (this.M + this.R + eps), 0, 1);
    const bullish = this.M * p.alpha * (1 - regressPressure);
    const bearish = this.R * currentGamma * regressPressure;

    this.M -= this.M * p.alpha;
    this.M = Math.max(0, this.M);
    this.R += bullish;
    this.R -= bearish;
    const rDecay = p.regressDecayBase * (1 + this.trust * p.trustCoupling);
    this.R -= this.R * rDecay;
    this.R = Math.max(0, this.R);

    this.E += bullish - bearish;
    this.E += (0 - this.E) * p.delta;
    this.E = clamp(this.E, -1, 1);

    this._lastBullBear = { bullish, bearish, regressPressure };

    // Step 8: Perturbation layer (stepped OU, every perturbK ticks)
    this.perturbCounter++;
    let deltaP = 0;
    if (this.perturbCounter >= this.perturbK) {
      const k = this.perturbK;
      const decayFactor = Math.pow(1 - p.lambda, k);
      const noise = p.sigma * Math.sqrt(k / TIME_CONFIG.ticksPerDay) * boxMuller();
      this.perturbV = this.perturbV * decayFactor + noise;
      this.perturbV = clamp(this.perturbV, -1, 1);
      this.perturbCounter = 0;
    }
    deltaP = this.perturbV * p.maxPerturbAmplitude;

    // Step 9: Price settlement
    const deltaT = this.E * p.trendScale;
    const newPrice = this.price * (1 + deltaT + deltaP);
    this.prevPrice = this.price;
    this.price = Math.max(0.01, newPrice);

    const wasAtHigh = this.price > this.historicalHigh;
    this.historicalHigh = Math.max(this.historicalHigh, this.price);

    // Harvest: automatic passive harvest
    if (this.price > this.initPrice) {
      const harvestAmount = Math.max(0, (this.price - this.initPrice)) *
        (this.warmAudience + this.coldAudience) * 0.00001;
      this.poolHarvested = Math.min(this.poolTotal, this.poolHarvested + harvestAmount);
    }

    // Step 10: priceVelocity update
    if (wasAtHigh) {
      this.priceVelocity = 1.0;
    } else {
      this.priceVelocity = Math.max(0,
        this.priceVelocity * (1 - p.velocityDecay * (1 - this.heat)));
    }

    // Regulatory risk natural slow decay
    this.regulatoryRisk = Math.max(0, this.regulatoryRisk - 0.0001);

    // Regulatory threshold events
    this._checkRegulatoryThresholds();

    // Step 11: Update timestamp and price history
    this.timestamp++;
    this.priceHistory.push(this.price);
    if (this.priceHistory.length > 500) this.priceHistory.shift();

    // Invalidate snapshot cache
    this._snapshot = null;

    // Step 12: Notify CandleBuilder
    if (this.onCandleTick) this.onCandleTick(this.price, this.getSnapshot());
  }

  _checkRegulatoryThresholds() {
    if (this.regulatoryRisk > 0.95 && this.marketConfidence > 0) {
      this.marketConfidence = 0;
      if (this.onRegEvent) this.onRegEvent('liquidation');
      if (this.onGameOver) this.onGameOver('regulatory_liquidation');
    } else if (this.regulatoryRisk > 0.8 && this.marketConfidence > 0.1) {
      this.marketConfidence = Math.max(0, this.marketConfidence - 0.001);
      if (this.onRegEvent) this.onRegEvent('investigation');
    } else if (this.regulatoryRisk > 0.5) {
      if (this.onRegEvent) this.onRegEvent('warning');
    }
  }

  // ============================================================
  // Public snapshot (read-only view for all external systems)
  // ============================================================
  getSnapshot() {
    if (this._snapshot) return this._snapshot;
    this._snapshot = {
      price: this.price,
      historicalHigh: this.historicalHigh,
      priceHistory: this.priceHistory,

      // Surface params
      trust: this.trust,
      heat: this.heat,
      surfaceEmotion: this.surfaceEmotion,
      audience: this.warmAudience + this.coldAudience,
      warmAudience: this.warmAudience,
      coldAudience: this.coldAudience,
      maxAudience: this.maxAudience,

      // Core engine
      momentum: this.M,
      emotion: this.E,
      regressForce: this.R,
      regressPressure: this._lastBullBear.regressPressure ?? 0,
      bullish: this._lastBullBear.bullish ?? 0,
      bearish: this._lastBullBear.bearish ?? 0,

      // M gen debug
      effectiveReach: this._lastMGen.effectiveReach,
      baseGen: this._lastMGen.baseGen,
      fomoGen: this._lastMGen.fomoGen,
      mDelta: this._lastMGen.delta,

      // Regulatory
      regulatoryRisk: this.regulatoryRisk,
      marketConfidence: this.marketConfidence,

      // Pool
      poolTotal: this.poolTotal,
      poolHarvested: this.poolHarvested,
      harvestRatio: this.poolHarvested / (this.poolTotal || 1),

      priceVelocity: this.priceVelocity,
      timestamp: this.timestamp,
    };
    return this._snapshot;
  }

  // Direct state setter used only by Director system (sets rates, not values)
  applyParamOverride(overrides) {
    Object.assign(params, overrides);
  }
}
