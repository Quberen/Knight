'use strict';

// Single candle accumulator
class CandleAccum {
  constructor(open) {
    this.open = open;
    this.high = open;
    this.low = open;
    this.currentClose = open;
    this.tickCount = 0;
    this.volumeAccum = 0;
  }

  update(price, audience, maxAudience) {
    this.high = Math.max(this.high, price);
    this.low = Math.min(this.low, price);
    this.currentClose = price;
    this.tickCount++;
    const priceAmp = Math.abs(price - this.open) / (this.open + 1e-9);
    this.volumeAccum += this.tickCount * priceAmp * (1 + (audience / (maxAudience || 1)));
  }

  seal() {
    return {
      open: this.open,
      high: this.high,
      low: this.low,
      close: this.currentClose,
      volume: this.volumeAccum,
      tickCount: this.tickCount,
    };
  }

  // Live (unsealed) candle for real-time rendering
  live(timestamp) {
    return {
      open: this.open,
      high: this.high,
      low: this.low,
      close: this.currentClose,
      volume: this.volumeAccum,
      tickCount: this.tickCount,
      timestamp,
      live: true,
    };
  }
}

// CandleBuilder — three-level hierarchy (hourly base, daily, weekly)
class CandleBuilder {
  constructor() {
    const initPrice = 100;

    // Base: hourly (10 ticks per candle, i.e. ticksPerHour = ticksPerDay/24 = 10)
    this.hourly = new CandleAccum(initPrice);
    this.hourlyHistory = []; // sealed hourly candles
    this.ticksInHour = 0;

    // Daily (24 hourly candles)
    this.daily = new CandleAccum(initPrice);
    this.dailyHistory = [];
    this.hoursInDay = 0;

    // Weekly (7 daily candles)
    this.weekly = new CandleAccum(initPrice);
    this.weeklyHistory = [];
    this.daysInWeek = 0;

    // Tick counter
    this.totalTicks = 0;

    // Callbacks
    this.onHourlySealed = null;
    this.onDailySealed = null;
    this.onWeeklySealed = null;
  }

  get ticksPerHour() {
    return TIME_CONFIG.ticksPerHour; // = 10
  }

  // Called every tick from PriceEngine step 12
  push(price, snapshot) {
    const audience = snapshot.audience;
    const maxAudience = snapshot.maxAudience;

    // Update hourly accumulator
    this.hourly.update(price, audience, maxAudience);
    this.ticksInHour++;
    this.totalTicks++;

    // Seal hourly candle?
    if (this.ticksInHour >= this.ticksPerHour) {
      const sealed = this.hourly.seal();
      sealed.timestamp = this.totalTicks;
      sealed.level = 'hourly';
      this.hourlyHistory.push(sealed);
      if (this.onHourlySealed) this.onHourlySealed(sealed);

      // Feed into daily accumulator
      this.daily.high = Math.max(this.daily.high, sealed.high);
      this.daily.low = Math.min(this.daily.low, sealed.low);
      this.daily.currentClose = sealed.close;
      this.daily.volumeAccum += sealed.volume;
      this.daily.tickCount += sealed.tickCount;
      this.hoursInDay++;

      // Seal daily candle? (24 hours per day)
      if (this.hoursInDay >= 24) {
        const dSealed = this.daily.seal();
        dSealed.timestamp = this.totalTicks;
        dSealed.level = 'daily';
        this.dailyHistory.push(dSealed);
        if (this.onDailySealed) this.onDailySealed(dSealed);

        // Feed into weekly accumulator
        this.weekly.high = Math.max(this.weekly.high, dSealed.high);
        this.weekly.low = Math.min(this.weekly.low, dSealed.low);
        this.weekly.currentClose = dSealed.close;
        this.weekly.volumeAccum += dSealed.volume;
        this.weekly.tickCount += dSealed.tickCount;
        this.daysInWeek++;

        // Seal weekly candle? (7 days per week)
        if (this.daysInWeek >= 7) {
          const wSealed = this.weekly.seal();
          wSealed.timestamp = this.totalTicks;
          wSealed.level = 'weekly';
          this.weeklyHistory.push(wSealed);
          if (this.onWeeklySealed) this.onWeeklySealed(wSealed);

          // Start new weekly accumulator (open = last close)
          this.weekly = new CandleAccum(wSealed.close);
          this.daysInWeek = 0;
        }

        // Start new daily accumulator
        this.daily = new CandleAccum(dSealed.close);
        this.hoursInDay = 0;
      }

      // Start new hourly accumulator
      this.hourly = new CandleAccum(sealed.close);
      this.ticksInHour = 0;
    }
  }

  // Get candles for the requested view level
  // Returns: { candles: [...sealed], live: <current accumulator live candle> }
  getView(level) {
    switch (level) {
      case '6H': {
        // Show last N hourly candles representing 6 hours of data
        // 6H view: show last 6 hourly bars per visible segment
        return {
          candles: this.hourlyHistory.slice(-60),
          live: this.hourly.live(this.totalTicks),
        };
      }
      case '1D': {
        return {
          candles: this.dailyHistory.slice(-30),
          live: this.daily.live(this.totalTicks),
        };
      }
      case '1W': {
        return {
          candles: this.weeklyHistory.slice(-12),
          live: this.weekly.live(this.totalTicks),
        };
      }
      default:
        return {
          candles: this.hourlyHistory.slice(-60),
          live: this.hourly.live(this.totalTicks),
        };
    }
  }
}
