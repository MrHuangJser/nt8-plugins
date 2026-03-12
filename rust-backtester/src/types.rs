use serde::Serialize;

/// 秒级 bar 数据
#[derive(Debug, Clone)]
pub struct SecondBar {
    pub timestamp: i64, // unix epoch seconds
    pub open: f64,
    pub high: f64,
    pub low: f64,
    pub close: f64,
    pub volume: u64,
}

/// 5 分钟 bar 数据，包含指向秒级数据的索引范围
#[derive(Debug, Clone)]
pub struct FiveMinBar {
    pub timestamp: i64,
    pub open: f64,
    pub high: f64,
    pub low: f64,
    pub close: f64,
    pub volume: u64,
    /// 该 5m bar 内秒级 bar 的起始索引（含）
    pub second_bar_start: usize,
    /// 该 5m bar 内秒级 bar 的结束索引（不含）
    pub second_bar_end: usize,
}

/// 策略状态机
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum State {
    Idle,
    WaitingConfirmation,
    StopPending,
    EntryFilled,
}

/// 交易方向
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Direction {
    Long,
    Short,
}

/// 策略方向配置
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum StrategyDirection {
    Both,
    LongOnly,
    ShortOnly,
}

/// 订单类型
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum OrderType {
    StopMarket,
    Limit,
}

/// 挂单
#[derive(Debug, Clone)]
pub struct PendingOrder {
    pub order_type: OrderType,
    pub price: f64,
    pub direction: Direction,
    pub signal_name: String,
}

/// 持仓腿
#[derive(Debug, Clone)]
pub struct PositionLeg {
    pub entry_name: String,
    pub fill_price: f64,
    pub tp_price: f64,
    pub sl_price: f64,
    pub is_active: bool,
}

/// 持仓
#[derive(Debug, Clone)]
pub struct Position {
    pub direction: Option<Direction>,
    pub legs: Vec<PositionLeg>,
}

impl Position {
    pub fn new() -> Self {
        Self {
            direction: None,
            legs: Vec::new(),
        }
    }

    pub fn is_flat(&self) -> bool {
        self.legs.iter().all(|l| !l.is_active)
    }

    pub fn active_count(&self) -> usize {
        self.legs.iter().filter(|l| l.is_active).count()
    }

    pub fn average_price(&self) -> f64 {
        let active: Vec<_> = self.legs.iter().filter(|l| l.is_active).collect();
        if active.is_empty() {
            return 0.0;
        }
        let sum: f64 = active.iter().map(|l| l.fill_price).sum();
        sum / active.len() as f64
    }
}

/// 策略状态
#[derive(Debug, Clone)]
pub struct StrategyState {
    pub state: State,
    // MB
    pub mb_high: f64,
    pub mb_low: f64,
    pub mb_range: f64,
    pub mb_form_bar: usize, // 5m bar index
    pub mb_id: u32,
    // 确认与方向
    pub direction: Option<Direction>,
    pub confirm_bar_high: f64,
    pub confirm_bar_low: f64,
    // 订单
    pub stop_order: Option<PendingOrder>,
    pub addon_order: Option<PendingOrder>,
    pub addon2_order: Option<PendingOrder>,
    // 成交状态
    pub stop_fill_price: f64,
    pub addon1_filled: bool,
    pub addon2_filled: bool,
    pub pending_be_update: bool,
    // 持仓
    pub position: Position,
}

impl StrategyState {
    pub fn new() -> Self {
        Self {
            state: State::Idle,
            mb_high: 0.0,
            mb_low: 0.0,
            mb_range: 0.0,
            mb_form_bar: 0,
            mb_id: 0,
            direction: None,
            confirm_bar_high: 0.0,
            confirm_bar_low: 0.0,
            stop_order: None,
            addon_order: None,
            addon2_order: None,
            stop_fill_price: 0.0,
            addon1_filled: false,
            addon2_filled: false,
            pending_be_update: false,
            position: Position::new(),
        }
    }

    pub fn reset(&mut self) {
        self.state = State::Idle;
        self.mb_high = 0.0;
        self.mb_low = 0.0;
        self.mb_range = 0.0;
        self.mb_form_bar = 0;
        self.direction = None;
        self.confirm_bar_high = 0.0;
        self.confirm_bar_low = 0.0;
        self.stop_order = None;
        self.addon_order = None;
        self.addon2_order = None;
        self.stop_fill_price = 0.0;
        self.addon1_filled = false;
        self.addon2_filled = false;
        self.pending_be_update = false;
        self.position = Position::new();
    }
}

/// 策略参数
#[derive(Debug, Clone, Serialize)]
pub struct StrategyParams {
    pub strategy_direction: u8, // 0=Both, 1=LongOnly, 2=ShortOnly
    pub trade_start_time: i32,  // HHMMSS (UTC+8)
    pub trade_end_time: i32,    // HHMMSS (UTC+8)
    pub utc_offset_hours: i32,  // 交易所 UTC 偏移
    pub long_tp_pct: f64,
    pub short_tp_pct: f64,
    pub enable_addon: bool,
    pub long_addon_pct: f64,
    pub short_addon_pct: f64,
    pub enable_addon2: bool,
    pub long_addon2_pct: f64,
    pub short_addon2_pct: f64,
    pub tick_size: f64,
}

impl StrategyParams {
    pub fn default_es() -> Self {
        Self {
            strategy_direction: 0, // Both
            trade_start_time: 063000,
            trade_end_time: 210000,
            utc_offset_hours: 8,
            long_tp_pct: 1.618,
            short_tp_pct: -0.618,
            enable_addon: true,
            long_addon_pct: 0.725,
            short_addon_pct: 0.27,
            enable_addon2: true,
            long_addon2_pct: 0.27,
            short_addon2_pct: 0.725,
            tick_size: 0.25, // ES tick size
        }
    }

    pub fn direction(&self) -> StrategyDirection {
        match self.strategy_direction {
            1 => StrategyDirection::LongOnly,
            2 => StrategyDirection::ShortOnly,
            _ => StrategyDirection::Both,
        }
    }
}

/// 已完成的交易记录
#[derive(Debug, Clone, Serialize)]
pub struct Trade {
    pub entry_time: i64,
    pub exit_time: i64,
    pub direction: String,
    pub signal_name: String,
    pub entry_price: f64,
    pub exit_price: f64,
    pub pnl: f64,
    pub exit_reason: String, // "TP" or "SL" or "SessionClose"
}

/// 回测结果
#[derive(Debug, Clone, Serialize)]
pub struct BacktestResult {
    pub params: StrategyParams,
    pub trades: Vec<Trade>,
    pub net_profit: f64,
    pub max_drawdown: f64,
    pub win_rate: f64,
    pub trade_count: usize,
    pub profit_factor: f64,
    pub sharpe_ratio: f64,
}
