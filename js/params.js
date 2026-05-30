'use strict';

// ParamEnvelope — all parameters read from here, zero hardcoding in engine
const DEFAULT_PARAMS = {
  // Perturbation layer (OU process)
  sigma: 0.3,
  lambda: 0.5,
  maxPerturbAmplitude: 0.015,

  // Core engine - Scheme C
  alpha: 0.025,        // M → E release rate
  delta: 0.01,         // E natural decay
  gamma: 0.08,         // R release rate
  trendScale: 0.02,    // E → price scale
  regressDecayBase: 0.005,
  trustCoupling: 0.8,

  // M generation layer
  convRate: 0.08,
  fomoScale: 0.15,
  trustExponent: 1.5,
  velocityDecay: 0.02,

  // Audience infection model
  baseSpread: 0.001,
  priceBoost: 3.0,
  warmRate: 0.005,
  coldFactor: 0.4,
  churnRate: 0.0005,

  // Surface parameter decay
  heatDecay: 0.03,
  emotionDecay: 0.05,

  // Render filter thresholds
  wickThreshold: 0.002,
  bodyThreshold: 0.001,
  yAxisMinRange: 0.02,
  yAxisPadding: 0.15,
};

// Time config — adjustable from dev panel
const TIME_CONFIG = {
  ticksPerDay: 240,
  realSecondsPerDay: 20,
  get tickIntervalMs() {
    return (this.realSecondsPerDay / this.ticksPerDay) * 1000;
  },
  get ticksPerHour() {
    return Math.floor(this.ticksPerDay / 24); // = 10
  },
};

// Live params object (copy of defaults, mutated by dev panel)
const params = Object.assign({}, DEFAULT_PARAMS);
