# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**炒作模拟器 (Hype Simulator)** — a Bloomberg terminal-style browser strategy game. The player acts as a behind-the-scenes market manipulator, using Action Points (AP) to run operations that push Trust, Heat, and Emotion dials, driving price action and harvesting profit before regulatory pressure ends the game.

## Running the Game

No build step — pure vanilla JS. Open `index.html` directly in a browser, or serve it with any static server:

```bash
python3 -m http.server 8080
# then open http://localhost:8080
```

Syntax-check all JS files:
```bash
node --check js/params.js js/engine.js js/candle.js js/renderer.js js/events.js js/gameloop.js js/ui.js js/main.js
```

## Script Load Order

`index.html` loads scripts in strict dependency order — do not reorder:

```
params.js → engine.js → candle.js → renderer.js → events.js → gameloop.js → ui.js → main.js
```

Each file relies on globals defined by earlier files (`params`, `TIME_CONFIG`, `PriceEngine`, `CandleBuilder`, etc.).

## Architecture

### Core Data Flow

```
GameLoop.tick()
  → EventSystem.tick()         — random event (every 80–140 ticks)
  → PriceEngine.tick(input)    — 12-step state evolution
  → CandleBuilder.push(price)  — feeds candle hierarchy
  → UIManager.updateAll()      — DOM updates (throttled 100ms)
  → ChartRenderer.render()     — canvas K-line (every rAF)
```

### PriceEngine (`js/engine.js`)

Pure state machine. The 12-step tick in strict order:
1. Read `params` (ParamEnvelope — never hardcode values)
2. Apply operation input (heatDelta, trustDelta, momentumInject, etc.)
3. Process active overrides (gammaOverride countdown)
4. Natural decay (heat, surfaceEmotion)
5. Audience infection (organic spread → cold→warm conversion → churn)
6. M generation (effectiveReach × trust × surfaceEmotion × convRate + FOMO)
7. Scheme C core: `regressPressure = R/(M+R+ε)` → bullish/bearish tug-of-war
8. Perturbation layer (stepped OU process, Box-Muller noise every k=5 ticks)
9. Price settlement: `price × (1 + E×trendScale + perturbV×maxPerturbAmplitude)`
10. priceVelocity update
11. Regulatory threshold checks (>0.5 warn / >0.8 investigation / >0.95 liquidation)
12. Timestamp + snapshot cache invalidation

`getSnapshot()` returns a cached read-only view; cache is invalidated each tick.

### Parameters (`js/params.js`)

All engine constants live in `params` (mutable copy of `DEFAULT_PARAMS`). `TIME_CONFIG` holds tick timing. The debug panel mutates `params` live at runtime — never hardcode engine values in `engine.js`.

### CandleBuilder (`js/candle.js`)

Three-level hierarchy built from `CandleAccum` objects:
- **Hourly**: seals every 10 ticks (`TIME_CONFIG.ticksPerHour`)
- **Daily**: aggregates 24 hourly candles
- **Weekly**: aggregates 7 daily candles

`getView('6H' | '1D' | '1W')` returns `{ candles: [...sealed], live: <current accum> }`.

### Renderer (`js/renderer.js`)

Canvas-based. `getRenderDecision(candle)` classifies each candle as `FULL`, `DOJI`, or `THIN_LINE` based on `bodyChangeRatio` and `totalRangeRatio` vs thresholds in `params`. Handles `devicePixelRatio` in `_resize()`. The live (unsealed) candle is rendered in amber.

### GameLoop (`js/gameloop.js`)

State machine: `PRE_LAUNCH (480t) → WORKDAY (240t×5) → SETTLEMENT (60t) → WEEKEND (240t) → WORKDAY → …`

Operations cost only **AP** — USD is never consumed. USD is earned passively each tick via harvest (`price > initPrice`). Victory requires `harvestRatio ≥ 0.7 AND regulatoryRisk < 0.8 AND marketConfidence > 0`.

### UI (`js/ui.js`)

`UIManager.init()` wires all DOM refs. Debug panel slides in from the right edge via CSS `transform: translateX`. Backtick key or DEBUG button toggles it; swipe-right gesture closes it on mobile. `updateAll()` is throttled to 100ms.

### Globals exposed on `window`

`_gameLoop`, `_engine`, `_eventSystem`, `_params` — available for console debugging.

## Key Design Constraints

- **USD is not consumable** — operations only spend AP; USD is a score metric.
- **No hardcoded engine values** — all constants must go through `params`.
- **Mobile-first** — minimum 44px touch targets; canvas uses `devicePixelRatio`; debug panel supports swipe-to-close. Breakpoints at 480px and 360px (left column hidden at 360px).
