'use strict';

// Random event pool — 15 events
const EVENT_POOL = [
  { id: 'kol_viral',      text: '某大V发文力挺，评论区热度暴涨', effect: { heatDelta: 0.12, emotionDelta: 0.08 } },
  { id: 'media_doubt',    text: '财经媒体发文质疑，市场出现杂音', effect: { trustDelta: -0.08, regulatoryRiskAdd: 0.04 } },
  { id: 'institution',    text: '传言某机构低调建仓，聪明钱入场', effect: { trustDelta: 0.07, heatDelta: 0.05 } },
  { id: 'fomo_post',      text: '社区帖子病毒式传播："这次不一样"', effect: { heatDelta: 0.15, emotionDelta: 0.12 } },
  { id: 'reg_notice',     text: '监管部门发布风险提示公告', effect: { trustDelta: -0.06, regulatoryRiskAdd: 0.08 } },
  { id: 'celebrity',      text: '知名人士公开持有，引发模仿效应', effect: { heatDelta: 0.10, audiencePurchased: 80 } },
  { id: 'competitor',     text: '竞品出现，部分资金分流', effect: { heatDelta: -0.05, trustDelta: -0.03 } },
  { id: 'tech_upgrade',   text: '技术升级公告发布，社区情绪高涨', effect: { trustDelta: 0.05, emotionDelta: 0.07 } },
  { id: 'sell_rumor',     text: '大户减仓传言，市场情绪转为观望', effect: { heatDelta: -0.08, trustDelta: -0.05 } },
  { id: 'airdrop_hype',   text: '空投消息引发入场潮', effect: { audiencePurchased: 150, heatDelta: 0.08 } },
  { id: 'exchange_list',  text: '主流交易所即将上架', effect: { trustDelta: 0.10, heatDelta: 0.10, emotionDelta: 0.10 } },
  { id: 'hack_scare',     text: '网络安全事件波及行业，信任受损', effect: { trustDelta: -0.12, regulatoryRiskAdd: 0.06 } },
  { id: 'policy_ease',    text: '政策传闻利好，市场信心略微修复', effect: { trustDelta: 0.04, regulatoryRiskAdd: -0.03 } },
  { id: 'ponzi_exposed',  text: '关联项目被曝光为庞氏骗局', effect: { trustDelta: -0.15, regulatoryRiskAdd: 0.12 } },
  { id: 'quiet_period',   text: '市场进入沉寂期，成交量萎缩', effect: { heatDelta: -0.04 } },
];

class EventSystem {
  constructor() {
    this.nextEventIn = this._nextInterval();
    this.tickCounter = 0;
    this.recentEvents = []; // shown in news panel
    this.onEvent = null; // callback(event)
  }

  _nextInterval() {
    return 80 + Math.floor(Math.random() * 60); // 80–140 ticks
  }

  tick() {
    this.tickCounter++;
    if (this.tickCounter >= this.nextEventIn) {
      this.tickCounter = 0;
      this.nextEventIn = this._nextInterval();
      const ev = EVENT_POOL[Math.floor(Math.random() * EVENT_POOL.length)];
      this._trigger(ev);
      return ev;
    }
    return null;
  }

  _trigger(ev) {
    const ts = new Date().toLocaleTimeString('zh', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    this.recentEvents.unshift({ ...ev, ts });
    if (this.recentEvents.length > 8) this.recentEvents.pop();
    if (this.onEvent) this.onEvent(ev);
  }

  // Manually push a system message (regulatory events, etc.)
  pushSystem(text) {
    const ts = new Date().toLocaleTimeString('zh', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    this.recentEvents.unshift({ id: 'system', text, ts });
    if (this.recentEvents.length > 8) this.recentEvents.pop();
  }
}
