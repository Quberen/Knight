'use strict';

// ============================================================
// UI Manager — updates DOM panels, manages debug panel
// ============================================================
class UIManager {
  constructor(gameLoop, engine, renderer, candleBuilder) {
    this.gameLoop = gameLoop;
    this.engine = engine;
    this.renderer = renderer;
    this.candleBuilder = candleBuilder;

    // Current chart view
    this.chartView = '6H';

    // Debug panel open state
    this.debugOpen = false;

    // Panel animation state
    this._debugSliding = false;

    // DOM refs (populated in init)
    this.els = {};

    // Throttle DOM updates
    this._lastDomUpdate = 0;

    // Last render decision for debug
    this._lastRenderDecision = null;
  }

  // ============================================================
  // Init — wire up DOM
  // ============================================================
  init() {
    const q = (sel) => document.querySelector(sel);
    const qa = (sel) => document.querySelectorAll(sel);

    this.els = {
      // Header
      dayLabel: q('#day-label'),
      priceHeader: q('#price-header'),

      // Chart canvas
      chartCanvas: q('#chart-canvas'),

      // Vitals
      trustBar: q('#trust-bar'), trustVal: q('#trust-val'),
      heatBar: q('#heat-bar'), heatVal: q('#heat-val'),
      emotionBar: q('#emotion-bar'), emotionVal: q('#emotion-val'),
      audienceVal: q('#audience-val'),
      maxAudienceVal: q('#max-audience-val'),

      // E-Layer (core engine state)
      eVal: q('#e-val'),
      mVal: q('#m-val'),
      rVal: q('#r-val'),
      rpVal: q('#rp-val'),
      rActiveLabel: q('#r-active-label'),

      // Resources
      usdVal: q('#usd-val'),
      apVal: q('#ap-val'),
      apMax: q('#ap-max'),
      regBar: q('#reg-bar'), regVal: q('#reg-val'),
      confBar: q('#conf-bar'), confVal: q('#conf-val'),
      harvestBar: q('#harvest-bar'), harvestVal: q('#harvest-val'),

      // Operations
      opsPanel: q('#ops-panel'),

      // News
      newsPanel: q('#news-panel'),

      // Time view buttons
      viewBtns: qa('.view-btn'),

      // Debug panel
      debugPanel: q('#debug-panel'),
      debugToggleBtn: q('#debug-toggle-btn'),

      // Debug sliders (populated dynamically)
      debugSliders: q('#debug-sliders'),
      debugState: q('#debug-state'),
      debugExport: q('#debug-export'),

      // Overlay
      overlay: q('#game-overlay'),
      overlayTitle: q('#overlay-title'),
      overlayBody: q('#overlay-body'),
      overlayBtn: q('#overlay-btn'),
    };

    this._buildOpButtons();
    this._buildDebugSliders();
    this._wireViewButtons();
    this._wireDebugPanel();
    this._wireKeyboard();
    this._wireTouchDebug();
  }

  // ============================================================
  // Build operation buttons
  // ============================================================
  _buildOpButtons() {
    const panel = this.els.opsPanel;
    if (!panel) return;
    panel.innerHTML = '';

    Object.values(OPERATIONS).forEach(op => {
      const btn = document.createElement('button');
      btn.className = 'op-btn';
      btn.dataset.opId = op.id;
      btn.innerHTML = `
        <span class="op-name">${op.name}</span>
        <span class="op-ap">AP×${op.ap}</span>
      `;
      btn.title = op.desc;
      btn.addEventListener('click', () => this._onOpClick(op.id));
      panel.appendChild(btn);
    });
  }

  _onOpClick(opId) {
    const result = this.gameLoop.executeOperation(opId);
    if (!result.success) {
      this._flashError(opId, result.reason);
    } else {
      this._flashSuccess(opId);
      this.updateAll();
    }
  }

  _flashError(opId, reason) {
    const btn = this.els.opsPanel.querySelector(`[data-op-id="${opId}"]`);
    if (!btn) return;
    btn.classList.add('op-error');
    setTimeout(() => btn.classList.remove('op-error'), 400);
  }

  _flashSuccess(opId) {
    const btn = this.els.opsPanel.querySelector(`[data-op-id="${opId}"]`);
    if (!btn) return;
    btn.classList.add('op-success');
    setTimeout(() => btn.classList.remove('op-success'), 300);
  }

  // ============================================================
  // Build debug sliders
  // ============================================================
  _buildDebugSliders() {
    const container = this.els.debugSliders;
    if (!container) return;
    container.innerHTML = '';

    const SLIDER_DEFS = [
      { key: 'realSecondsPerDay', label: 'Speed (s/day)', min: 5, max: 60, step: 1, src: 'time' },
      { key: 'alpha',    label: 'α (M→E)',        min: 0.001, max: 0.1,  step: 0.001 },
      { key: 'delta',    label: 'δ (E decay)',     min: 0.001, max: 0.05, step: 0.001 },
      { key: 'gamma',    label: 'γ (R release)',   min: 0.01,  max: 0.3,  step: 0.005 },
      { key: 'trendScale', label: 'trendScale',    min: 0.005, max: 0.05, step: 0.001 },
      { key: 'regressDecayBase', label: 'R decay base', min: 0.001, max: 0.02, step: 0.001 },
      { key: 'sigma',    label: 'σ (noise)',       min: 0.05,  max: 1.5,  step: 0.05 },
      { key: 'lambda',   label: 'λ (OU revert)',   min: 0.1,   max: 0.9,  step: 0.05 },
      { key: 'maxPerturbAmplitude', label: 'Perturb amp', min: 0.001, max: 0.05, step: 0.001 },
      { key: 'convRate', label: 'convRate',        min: 0.01,  max: 0.3,  step: 0.005 },
      { key: 'fomoScale', label: 'fomoScale',      min: 0.01,  max: 0.5,  step: 0.01 },
      { key: 'heatDecay', label: 'heatDecay',      min: 0.005, max: 0.1,  step: 0.005 },
      { key: 'emotionDecay', label: 'emotionDecay', min: 0.005, max: 0.15, step: 0.005 },
      { key: 'baseSpread', label: 'baseSpread',    min: 0.0001,max: 0.005, step: 0.0001 },
      { key: 'churnRate', label: 'churnRate',      min: 0.0001,max: 0.002, step: 0.0001 },
      { key: 'wickThreshold',  label: 'wickThresh', min: 0.0005, max: 0.01, step: 0.0005 },
      { key: 'bodyThreshold',  label: 'bodyThresh', min: 0.0002, max: 0.005, step: 0.0002 },
      { key: 'yAxisMinRange',  label: 'Y min range', min: 0.005, max: 0.1, step: 0.005 },
    ];

    SLIDER_DEFS.forEach(def => {
      const row = document.createElement('div');
      row.className = 'debug-row';

      const curVal = def.src === 'time' ? TIME_CONFIG[def.key] : params[def.key];

      row.innerHTML = `
        <span class="debug-label">${def.label}</span>
        <input type="range" class="debug-slider"
               data-key="${def.key}" data-src="${def.src || 'params'}"
               min="${def.min}" max="${def.max}" step="${def.step}"
               value="${curVal}">
        <span class="debug-value" id="dv-${def.key}">${Number(curVal).toPrecision(4)}</span>
      `;

      const slider = row.querySelector('input');
      const valEl = row.querySelector('.debug-value');

      slider.addEventListener('input', () => {
        const v = parseFloat(slider.value);
        valEl.textContent = Number(v).toPrecision(4);
        if (def.src === 'time') {
          TIME_CONFIG[def.key] = v;
          // Restart the tick interval
          if (window._restartTick) window._restartTick();
        } else {
          params[def.key] = v;
        }
      });

      container.appendChild(row);
    });

    // Export button
    if (this.els.debugExport) {
      this.els.debugExport.addEventListener('click', () => {
        const out = JSON.stringify({ params, timeConfig: { realSecondsPerDay: TIME_CONFIG.realSecondsPerDay } }, null, 2);
        console.log('=== PARAM EXPORT ===\n' + out);
        // Show in a temp textarea
        const ta = document.createElement('textarea');
        ta.value = out;
        ta.style.cssText = 'position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);width:80%;height:60%;z-index:9999;background:#1a1a2e;color:#00ff88;font-family:monospace;font-size:12px;border:1px solid #00ff88;padding:8px;';
        document.body.appendChild(ta);
        ta.select();
        setTimeout(() => ta.remove(), 8000);
      });
    }
  }

  // ============================================================
  // View buttons (6H / 1D / 1W)
  // ============================================================
  _wireViewButtons() {
    this.els.viewBtns.forEach(btn => {
      btn.addEventListener('click', () => {
        this.chartView = btn.dataset.view;
        this.els.viewBtns.forEach(b => b.classList.toggle('active', b === btn));
        this.renderChart();
      });
    });
    // Set initial active
    this.els.viewBtns.forEach(b => b.classList.toggle('active', b.dataset.view === this.chartView));
  }

  // ============================================================
  // Debug panel slide-in (from right)
  // ============================================================
  _wireDebugPanel() {
    const panel = this.els.debugPanel;
    const btn = this.els.debugToggleBtn;
    if (!panel || !btn) return;

    const toggle = () => {
      this.debugOpen = !this.debugOpen;
      panel.classList.toggle('open', this.debugOpen);
      btn.classList.toggle('active', this.debugOpen);
    };

    btn.addEventListener('click', toggle);
  }

  _wireKeyboard() {
    document.addEventListener('keydown', (e) => {
      if (e.key === '`' || e.key === '~') {
        this.debugOpen = !this.debugOpen;
        this.els.debugPanel.classList.toggle('open', this.debugOpen);
        this.els.debugToggleBtn.classList.toggle('active', this.debugOpen);
      }
    });
  }

  // Touch swipe to close debug panel (swipe right on panel → close)
  _wireTouchDebug() {
    const panel = this.els.debugPanel;
    if (!panel) return;
    let startX = 0;
    panel.addEventListener('touchstart', (e) => { startX = e.touches[0].clientX; }, { passive: true });
    panel.addEventListener('touchend', (e) => {
      const dx = e.changedTouches[0].clientX - startX;
      if (dx > 50) {
        this.debugOpen = false;
        panel.classList.remove('open');
        this.els.debugToggleBtn.classList.remove('active');
      }
    }, { passive: true });
  }

  // ============================================================
  // Render chart (called on rAF)
  // ============================================================
  renderChart() {
    const view = this.candleBuilder.getView(this.chartView);
    const snap = this.engine.getSnapshot();
    this.renderer.render(view, snap.price);
  }

  // ============================================================
  // Update all DOM panels (throttled to once per ~100ms)
  // ============================================================
  updateAll() {
    const now = performance.now();
    if (now - this._lastDomUpdate < 100) return;
    this._lastDomUpdate = now;

    const snap = this.engine.getSnapshot();
    const gl = this.gameLoop;

    // Header
    this._setText('day-label', gl.getDayLabel());
    this._setText('price-header', '$' + snap.price.toFixed(2));

    // Vitals bars
    this._setBar('trust', snap.trust);
    this._setBar('heat', snap.heat);
    this._setBar('emotion', snap.surfaceEmotion);
    this._setText('audience-val', Math.floor(snap.audience).toLocaleString());
    this._setText('max-audience-val', Math.floor(snap.maxAudience).toLocaleString());

    // E-Layer
    const rpPct = (snap.regressPressure * 100).toFixed(1);
    this._setText('e-val', (snap.emotion * 100).toFixed(1) + '%');
    this._setText('m-val', snap.momentum.toFixed(4));
    this._setText('r-val', snap.regressForce.toFixed(4));
    this._setText('rp-val', rpPct + '%');

    const standby = snap.regressPressure < 0.3;
    const rpLabel = this.els.rActiveLabel;
    if (rpLabel) {
      rpLabel.textContent = standby ? 'STANDBY' : 'ACTIVE';
      rpLabel.style.color = standby ? '#4a9' : '#f43';
    }

    // Resources
    this._setText('usd-val', '$' + gl.usd.toLocaleString());
    this._setText('ap-val', gl.actionPoints);
    this._setText('ap-max', gl.maxActionPoints);

    // Regulatory / confidence
    this._setBar('reg', snap.regulatoryRisk);
    const regEl = this.els.regBar;
    if (regEl) regEl.style.backgroundColor = snap.regulatoryRisk > 0.7 ? '#f43' : '#f80';
    this._setText('reg-val', (snap.regulatoryRisk * 100).toFixed(1) + '%');

    this._setBar('conf', snap.marketConfidence);
    this._setText('conf-val', (snap.marketConfidence * 100).toFixed(1) + '%');

    // Harvest
    this._setBar('harvest', snap.harvestRatio);
    this._setText('harvest-val', (snap.harvestRatio * 100).toFixed(1) + '%');

    // Operation buttons enable/disable
    this._updateOpButtons();

    // News panel
    this._updateNews();

    // Debug state panel
    if (this.debugOpen) this._updateDebugState(snap);
  }

  _setBar(prefix, ratio) {
    const bar = this.els[prefix + 'Bar'];
    if (bar) bar.style.width = (Math.min(1, Math.max(0, ratio)) * 100) + '%';
    const val = this.els[prefix + 'Val'];
    if (val && val.id !== prefix + '-val') return; // skip if handled elsewhere
  }

  _setText(id, text) {
    const el = this.els[id] || document.getElementById(id);
    if (el) el.textContent = text;
  }

  _updateOpButtons() {
    const panel = this.els.opsPanel;
    if (!panel) return;
    panel.querySelectorAll('.op-btn').forEach(btn => {
      const canDo = this.gameLoop.canExecute(btn.dataset.opId);
      btn.disabled = !canDo;
      btn.classList.toggle('disabled', !canDo);
    });
  }

  _updateNews() {
    const panel = this.els.newsPanel;
    if (!panel) return;
    const events = this.gameLoop.eventSystem.recentEvents;
    panel.innerHTML = events.slice(0, 6).map(ev =>
      `<div class="news-item"><span class="news-ts">${ev.ts}</span><span class="news-text">${ev.text}</span></div>`
    ).join('') || '<div class="news-empty">— 暂无新闻 —</div>';
  }

  _updateDebugState(snap) {
    const ds = this.els.debugState;
    if (!ds) return;
    ds.innerHTML = `
<div class="ds-section">表层参数</div>
<div class="ds-row"><span>Trust</span><span>${snap.trust.toFixed(3)}</span></div>
<div class="ds-row"><span>Heat</span><span>${snap.heat.toFixed(3)}</span></div>
<div class="ds-row"><span>Emotion</span><span>${snap.surfaceEmotion.toFixed(3)}</span></div>
<div class="ds-row"><span>Audience</span><span>${Math.floor(snap.audience)} / ${Math.floor(snap.maxAudience)}</span></div>
<div class="ds-section">M生成层</div>
<div class="ds-row"><span>effectiveReach</span><span>${snap.effectiveReach.toFixed(4)}</span></div>
<div class="ds-row"><span>baseGen</span><span>${snap.baseGen.toFixed(5)}</span></div>
<div class="ds-row"><span>fomoGen</span><span>${snap.fomoGen.toFixed(5)}</span></div>
<div class="ds-row"><span>ΔM</span><span>${snap.mDelta.toFixed(5)}</span></div>
<div class="ds-section">核心引擎</div>
<div class="ds-row"><span>M</span><span>${snap.momentum.toFixed(4)}</span></div>
<div class="ds-row"><span>E</span><span>${snap.emotion.toFixed(4)}</span></div>
<div class="ds-row"><span>R</span><span>${snap.regressForce.toFixed(4)}</span></div>
<div class="ds-row"><span>regressPressure</span><span>${snap.regressPressure.toFixed(4)}</span></div>
<div class="ds-row"><span>bullish</span><span>${snap.bullish.toFixed(5)}</span></div>
<div class="ds-row"><span>bearish</span><span>${snap.bearish.toFixed(5)}</span></div>
<div class="ds-section">渲染</div>
<div class="ds-row"><span>priceVelocity</span><span>${snap.priceVelocity.toFixed(3)}</span></div>
    `;
  }

  // ============================================================
  // Game over / victory overlay
  // ============================================================
  showOverlay(title, body, btnText, onClick) {
    const ov = this.els.overlay;
    if (!ov) return;
    this._setText('overlay-title', title);
    const bodyEl = document.getElementById('overlay-body');
    if (bodyEl) bodyEl.innerHTML = body;
    const btn = this.els.overlayBtn;
    if (btn) {
      btn.textContent = btnText;
      btn.onclick = onClick;
    }
    ov.classList.add('visible');
  }

  hideOverlay() {
    const ov = this.els.overlay;
    if (ov) ov.classList.remove('visible');
  }
}
