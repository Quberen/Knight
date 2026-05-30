'use strict';

// ============================================================
// RenderFilter — decides how to draw each candle
// ============================================================
function getRenderDecision(candle) {
  const bodyChangeRatio = Math.abs(candle.close - candle.open) / (candle.open + 1e-9);
  const totalRangeRatio = Math.abs(candle.high - candle.low) / (candle.open + 1e-9);

  let bodyStyle, wickStyle;
  if (totalRangeRatio < params.wickThreshold) {
    bodyStyle = 'THIN_LINE';
    wickStyle = 'HIDDEN';
  } else if (bodyChangeRatio < params.bodyThreshold) {
    bodyStyle = 'DOJI';
    wickStyle = 'FULL';
  } else {
    bodyStyle = 'FULL';
    wickStyle = 'FULL';
  }

  return { bodyStyle, wickStyle, bodyChangeRatio, totalRangeRatio };
}

// ============================================================
// ChartRenderer — canvas-based K-line chart
// ============================================================
class ChartRenderer {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.dpr = window.devicePixelRatio || 1;

    // Colors (trading terminal style)
    this.colors = {
      bg: '#0d1117',
      grid: 'rgba(255,255,255,0.06)',
      gridDashed: 'rgba(255,255,255,0.04)',
      up: '#00c878',     // bullish candle
      down: '#ff3a5c',   // bearish candle
      wick: null,        // same as body color
      priceLine: '#f0c040',
      priceLabel: '#f0c040',
      axisText: 'rgba(255,255,255,0.45)',
      axis: 'rgba(255,255,255,0.12)',
      live: 'rgba(240,192,64,0.6)',
    };

    this.yAxisWidth = 64;
    this.xAxisHeight = 24;
    this.padding = { top: 20, right: 4 };

    this._resize();
    window.addEventListener('resize', () => this._resize());
  }

  _resize() {
    const dpr = this.dpr;
    const rect = this.canvas.getBoundingClientRect();
    this.canvas.width = rect.width * dpr;
    this.canvas.height = rect.height * dpr;
    this.ctx.scale(dpr, dpr);
    this.W = rect.width;
    this.H = rect.height;
  }

  // ============================================================
  // Main render method
  // ============================================================
  render(view, currentPrice) {
    const { candles, live } = view;
    const ctx = this.ctx;
    const W = this.W;
    const H = this.H;

    if (!W || !H) return;

    // Clear
    ctx.clearRect(0, 0, W, H);
    ctx.fillStyle = this.colors.bg;
    ctx.fillRect(0, 0, W, H);

    const chartW = W - this.yAxisWidth - this.padding.right;
    const chartH = H - this.xAxisHeight - this.padding.top;
    const chartX = 0;
    const chartY = this.padding.top;

    // Combine sealed + live candle for Y axis range computation
    const allCandles = [...candles, live];

    // Y-axis range
    const { yMin, yMax } = this._computeYRange(allCandles, currentPrice);
    if (yMax <= yMin) return;

    const priceToY = (p) => chartY + chartH * (1 - (p - yMin) / (yMax - yMin));
    const yToPrice = (y) => yMin + (yMax - yMin) * (1 - (y - chartY) / chartH);

    // --- Draw background grid ---
    this._drawGrid(ctx, chartX, chartY, chartW, chartH, yMin, yMax, candles.length, priceToY);

    // --- Draw candles ---
    if (allCandles.length > 0) {
      // Enforce a minimum slot count so early candles don't stretch across the full chart
      const MIN_SLOTS = 30;
      const slotCount = Math.max(allCandles.length, MIN_SLOTS);
      const candleW = Math.max(2, Math.floor(chartW / slotCount));
      const gap = Math.max(1, Math.floor(candleW * 0.15));
      const bodyW = Math.max(1, candleW - gap * 2);

      allCandles.forEach((candle, i) => {
        if (!candle) return;
        const x = chartX + i * candleW;
        const centerX = x + candleW / 2;

        const decision = getRenderDecision(candle);
        const isUp = candle.close >= candle.open;
        const isLive = candle.live;
        const color = isLive ? this.colors.live : (isUp ? this.colors.up : this.colors.down);

        const openY = priceToY(candle.open);
        const closeY = priceToY(candle.close);
        const highY = priceToY(candle.high);
        const lowY = priceToY(candle.low);

        ctx.strokeStyle = color;
        ctx.fillStyle = color;
        ctx.lineWidth = 1;

        if (decision.bodyStyle === 'THIN_LINE') {
          // Market is still — draw a thin vertical line at mid price
          const midY = priceToY((candle.high + candle.low) / 2);
          ctx.globalAlpha = 0.4;
          ctx.beginPath();
          ctx.moveTo(centerX, highY);
          ctx.lineTo(centerX, lowY);
          ctx.stroke();
          ctx.globalAlpha = 1;
        } else {
          // Draw wick
          if (decision.wickStyle === 'FULL') {
            ctx.beginPath();
            ctx.moveTo(centerX, highY);
            ctx.lineTo(centerX, lowY);
            ctx.stroke();
          }

          // Draw body
          if (decision.bodyStyle === 'FULL') {
            const bodyTop = Math.min(openY, closeY);
            const bodyBot = Math.max(openY, closeY);
            const bodyH = Math.max(1, bodyBot - bodyTop);
            ctx.fillRect(x + gap, bodyTop, bodyW, bodyH);
          } else if (decision.bodyStyle === 'DOJI') {
            // Draw a horizontal doji line at close
            ctx.beginPath();
            ctx.moveTo(x + gap, closeY);
            ctx.lineTo(x + gap + bodyW, closeY);
            ctx.stroke();
          }
        }
      });
    }

    // --- Current price horizontal dashed line ---
    const priceY = priceToY(currentPrice);
    ctx.setLineDash([4, 4]);
    ctx.strokeStyle = this.colors.priceLine;
    ctx.lineWidth = 1;
    ctx.globalAlpha = 0.8;
    ctx.beginPath();
    ctx.moveTo(chartX, priceY);
    ctx.lineTo(chartX + chartW, priceY);
    ctx.stroke();
    ctx.setLineDash([]);
    ctx.globalAlpha = 1;

    // Price label on right (trading-software style)
    const priceStr = currentPrice.toFixed(2);
    ctx.fillStyle = this.colors.priceLine;
    ctx.font = `bold 11px 'Courier New', monospace`;
    ctx.textAlign = 'right';
    ctx.fillText('$' + priceStr, W - this.padding.right - 2, priceY + 4);
    ctx.textAlign = 'left';

    // --- Y axis labels ---
    this._drawYAxis(ctx, chartX, chartY, chartW, chartH, yMin, yMax, priceToY);

    // --- X axis labels ---
    this._drawXAxis(ctx, chartX, chartY, chartW, chartH, candles);
  }

  _computeYRange(candles, currentPrice) {
    let visibleHigh = currentPrice;
    let visibleLow = currentPrice;

    candles.forEach(c => {
      if (!c) return;
      visibleHigh = Math.max(visibleHigh, c.high);
      visibleLow = Math.min(visibleLow, c.low);
    });

    const rawRange = visibleHigh - visibleLow;
    const minRange = currentPrice * params.yAxisMinRange;

    if (rawRange < minRange) {
      const center = (visibleHigh + visibleLow) / 2;
      visibleHigh = center + minRange / 2;
      visibleLow = center - minRange / 2;
    }

    const padding = (visibleHigh - visibleLow) * params.yAxisPadding;
    const yMax = visibleHigh + padding;
    const yMin = Math.max(0, visibleLow - padding);

    return { yMin, yMax };
  }

  _drawGrid(ctx, chartX, chartY, chartW, chartH, yMin, yMax, candleCount, priceToY) {
    ctx.strokeStyle = this.colors.grid;
    ctx.lineWidth = 1;

    // Horizontal price grid lines (5 levels)
    const levels = 5;
    for (let i = 0; i <= levels; i++) {
      const price = yMin + (yMax - yMin) * (i / levels);
      const y = priceToY(price);
      ctx.setLineDash([2, 4]);
      ctx.beginPath();
      ctx.moveTo(chartX, y);
      ctx.lineTo(chartX + chartW, y);
      ctx.stroke();
    }
    ctx.setLineDash([]);

    // Vertical dashed lines (time divisions)
    const divs = Math.min(6, Math.max(2, Math.floor(candleCount / 5)));
    for (let i = 1; i < divs; i++) {
      const x = chartX + (chartW * i) / divs;
      ctx.strokeStyle = this.colors.gridDashed;
      ctx.setLineDash([3, 6]);
      ctx.beginPath();
      ctx.moveTo(x, chartY);
      ctx.lineTo(x, chartY + chartH);
      ctx.stroke();
    }
    ctx.setLineDash([]);
  }

  _drawYAxis(ctx, chartX, chartY, chartW, chartH, yMin, yMax, priceToY) {
    const levels = 5;
    ctx.fillStyle = this.colors.axisText;
    ctx.font = `10px 'Courier New', monospace`;
    ctx.textAlign = 'right';

    for (let i = 0; i <= levels; i++) {
      const price = yMin + (yMax - yMin) * (i / levels);
      const y = priceToY(price);
      if (y < chartY || y > chartY + chartH) continue;
      const label = price >= 1000 ? price.toFixed(0) : price.toFixed(2);
      ctx.fillText(label, this.W - this.padding.right - 2, y + 3);
    }
    ctx.textAlign = 'left';
  }

  _drawXAxis(ctx, chartX, chartY, chartW, chartH, candles) {
    if (!candles.length) return;
    ctx.fillStyle = this.colors.axisText;
    ctx.font = `9px 'Courier New', monospace`;
    ctx.textAlign = 'center';

    const every = Math.max(1, Math.floor(candles.length / 4));
    const candleW = chartW / Math.max(candles.length, 1);

    candles.forEach((c, i) => {
      if (i % every !== 0) return;
      const x = chartX + i * candleW + candleW / 2;
      const y = chartY + chartH + 14;
      const label = `T${c.timestamp || i}`;
      ctx.fillText(label, x, y);
    });
    ctx.textAlign = 'left';
  }
}
