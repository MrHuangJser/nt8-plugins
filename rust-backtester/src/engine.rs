use crate::types::*;

// ─── 辅助函数 ───

fn calc_level(state: &StrategyState, pct: f64) -> f64 {
    state.mb_low + state.mb_range * pct
}

fn round_to_tick(price: f64, tick_size: f64) -> f64 {
    (price / tick_size).round() * tick_size
}

fn floor_to_tick(price: f64, tick_size: f64) -> f64 {
    (price / tick_size).floor() * tick_size
}

fn ceil_to_tick(price: f64, tick_size: f64) -> f64 {
    (price / tick_size).ceil() * tick_size
}

/// 时段检查 (直译自 C# IsInTradeSession)
fn is_in_trade_session(timestamp: i64, params: &StrategyParams) -> bool {
    let dt = chrono::DateTime::from_timestamp(timestamp, 0).unwrap();
    let time_of_day = dt.format("%H%M%S").to_string().parse::<i32>().unwrap_or(0);

    let offset = 8 - params.utc_offset_hours;

    let start_h = params.trade_start_time / 10000;
    let start_m = (params.trade_start_time % 10000) / 100;
    let start_s = params.trade_start_time % 100;
    let end_h = params.trade_end_time / 10000;
    let end_m = (params.trade_end_time % 10000) / 100;
    let end_s = params.trade_end_time % 100;

    let start_total_s = start_h * 3600 + start_m * 60 + start_s - offset * 3600;
    let end_total_s = end_h * 3600 + end_m * 60 + end_s - offset * 3600;

    // 转为 HHMMSS
    let normalize = |mut s: i32| -> i32 {
        if s < 0 {
            s += 86400;
        }
        if s >= 86400 {
            s -= 86400;
        }
        let h = s / 3600;
        let m = (s % 3600) / 60;
        let sec = s % 60;
        h * 10000 + m * 100 + sec
    };

    let exchange_start = normalize(start_total_s);
    let exchange_end = normalize(end_total_s);

    if exchange_start > exchange_end {
        time_of_day >= exchange_start || time_of_day <= exchange_end
    } else {
        time_of_day >= exchange_start && time_of_day <= exchange_end
    }
}

fn can_long(params: &StrategyParams) -> bool {
    matches!(
        params.direction(),
        StrategyDirection::Both | StrategyDirection::LongOnly
    )
}

fn can_short(params: &StrategyParams) -> bool {
    matches!(
        params.direction(),
        StrategyDirection::Both | StrategyDirection::ShortOnly
    )
}

// ─── 状态机逻辑 ───

/// 检测 Mother Bar (Inside Bar)
fn detect_mother_bar(
    state: &mut StrategyState,
    bar: &FiveMinBar,
    prev_bar: &FiveMinBar,
    bar_idx: usize,
    params: &StrategyParams,
) {
    let is_inside = bar.high <= prev_bar.high
        && bar.low >= prev_bar.low
        && !(bar.high == prev_bar.high && bar.low == prev_bar.low);

    if is_inside {
        state.mb_high = prev_bar.high;
        state.mb_low = prev_bar.low;
        state.mb_range = state.mb_high - state.mb_low;
        state.mb_form_bar = bar_idx;
        state.mb_id += 1;
        state.state = State::WaitingConfirmation;
        state.direction = None;

        // 当前 bar 本身也可能是确认 K
        check_confirmation_signal(state, bar, params);
    }
}

/// 检查确认信号
fn check_confirmation_signal(state: &mut StrategyState, bar: &FiveMinBar, params: &StrategyParams) {
    if state.state != State::WaitingConfirmation {
        return;
    }

    let level_111 = calc_level(state, 1.11);
    let level_n11 = calc_level(state, -0.11);

    if can_long(params) && bar.close > level_111 {
        state.direction = Some(Direction::Long);
        state.confirm_bar_high = bar.high;
        state.confirm_bar_low = bar.low;
        state.state = State::StopPending;
        place_stop_entry(state, params);
    } else if can_short(params) && bar.close < level_n11 {
        state.direction = Some(Direction::Short);
        state.confirm_bar_high = bar.high;
        state.confirm_bar_low = bar.low;
        state.state = State::StopPending;
        place_stop_entry(state, params);
    }
}

/// 检查方向翻转
fn check_direction_flip(state: &mut StrategyState, bar: &FiveMinBar, params: &StrategyParams) {
    if state.state != State::StopPending {
        return;
    }

    let level_111 = calc_level(state, 1.11);
    let level_n11 = calc_level(state, -0.11);

    if state.direction == Some(Direction::Long) && can_short(params) && bar.close < level_n11 {
        state.stop_order = None;
        state.direction = Some(Direction::Short);
        state.confirm_bar_high = bar.high;
        state.confirm_bar_low = bar.low;
        place_stop_entry(state, params);
    } else if state.direction == Some(Direction::Short)
        && can_long(params)
        && bar.close > level_111
    {
        state.stop_order = None;
        state.direction = Some(Direction::Long);
        state.confirm_bar_high = bar.high;
        state.confirm_bar_low = bar.low;
        place_stop_entry(state, params);
    }
}

/// 检查失效
fn check_invalidation(state: &mut StrategyState, bar: &FiveMinBar) {
    let level_161_8 = calc_level(state, 1.618);
    let level_n61_8 = calc_level(state, -0.618);

    if bar.high >= level_161_8 || bar.low <= level_n61_8 {
        state.stop_order = None;
        state.reset();
        // 注意：reset 会把 mb_id 清零，但这里我们保留它（C# 只 reset 状态，不 reset mb_id）
    }
}

/// 检查更大 MB 替换
fn check_larger_mb_replacement(
    state: &mut StrategyState,
    bar: &FiveMinBar,
    prev_bar: &FiveMinBar,
    bar_idx: usize,
    params: &StrategyParams,
) {
    let is_inside = bar.high <= prev_bar.high
        && bar.low >= prev_bar.low
        && !(bar.high == prev_bar.high && bar.low == prev_bar.low);

    if !is_inside {
        return;
    }

    let new_high = prev_bar.high;
    let new_low = prev_bar.low;
    let new_range = new_high - new_low;

    if new_range > state.mb_range {
        state.stop_order = None;
        state.mb_high = new_high;
        state.mb_low = new_low;
        state.mb_range = new_range;
        state.mb_form_bar = bar_idx;
        state.mb_id += 1;
        state.state = State::WaitingConfirmation;
        state.direction = None;

        check_confirmation_signal(state, bar, params);
    }
}

// ─── 订单管理 ───

fn place_stop_entry(state: &mut StrategyState, params: &StrategyParams) {
    let tick = params.tick_size;

    match state.direction {
        Some(Direction::Long) => {
            let stop_price = round_to_tick(state.confirm_bar_high + tick, tick);

            state.stop_order = Some(PendingOrder {
                order_type: OrderType::StopMarket,
                price: stop_price,
                direction: Direction::Long,
                signal_name: "MB_BO_Long".to_string(),
            });
            state.position = Position::new();
        }
        Some(Direction::Short) => {
            let stop_price = round_to_tick(state.confirm_bar_low - tick, tick);

            state.stop_order = Some(PendingOrder {
                order_type: OrderType::StopMarket,
                price: stop_price,
                direction: Direction::Short,
                signal_name: "MB_BO_Short".to_string(),
            });
            state.position = Position::new();
        }
        None => {}
    }
}

fn place_addon_orders(state: &mut StrategyState, params: &StrategyParams) {
    let tick = params.tick_size;

    match state.direction {
        Some(Direction::Long) => {
            if params.enable_addon {
                let addon_price = round_to_tick(calc_level(state, params.long_addon_pct), tick);
                state.addon_order = Some(PendingOrder {
                    order_type: OrderType::Limit,
                    price: addon_price,
                    direction: Direction::Long,
                    signal_name: "MB_BO_Long_AddOn".to_string(),
                });
            }
            if params.enable_addon2 {
                let addon2_price = round_to_tick(calc_level(state, params.long_addon2_pct), tick);
                state.addon2_order = Some(PendingOrder {
                    order_type: OrderType::Limit,
                    price: addon2_price,
                    direction: Direction::Long,
                    signal_name: "MB_BO_Long_AddOn2".to_string(),
                });
            }
        }
        Some(Direction::Short) => {
            if params.enable_addon {
                let addon_price = round_to_tick(calc_level(state, params.short_addon_pct), tick);
                state.addon_order = Some(PendingOrder {
                    order_type: OrderType::Limit,
                    price: addon_price,
                    direction: Direction::Short,
                    signal_name: "MB_BO_Short_AddOn".to_string(),
                });
            }
            if params.enable_addon2 {
                let addon2_price = round_to_tick(calc_level(state, params.short_addon2_pct), tick);
                state.addon2_order = Some(PendingOrder {
                    order_type: OrderType::Limit,
                    price: addon2_price,
                    direction: Direction::Short,
                    signal_name: "MB_BO_Short_AddOn2".to_string(),
                });
            }
        }
        None => {}
    }
}

fn update_all_tps_to_breakeven(state: &mut StrategyState, tick_size: f64) {
    let be_price = round_to_tick(state.position.average_price(), tick_size);
    for leg in state.position.legs.iter_mut() {
        if leg.is_active {
            leg.tp_price = be_price;
        }
    }
    state.pending_be_update = false;
}

// ─── 订单匹配 ───

/// 检查秒级 bar 是否触发任何订单
fn check_order_fills(
    state: &mut StrategyState,
    bar: &SecondBar,
    params: &StrategyParams,
    trades: &mut Vec<Trade>,
) {
    let tick = params.tick_size;

    // 1. 检查持仓腿的 SL/TP（先检查 SL）
    check_position_exits(state, bar, trades);

    // 2. 检查 Stop 入场单
    if let Some(ref order) = state.stop_order.clone() {
        let filled = match order.direction {
            Direction::Long => bar.high >= order.price,
            Direction::Short => bar.low <= order.price,
        };

        if filled {
            let fill_price = order.price;
            state.stop_fill_price = fill_price;
            state.state = State::EntryFilled;

            // 计算 TP/SL
            let (tp, sl) = match order.direction {
                Direction::Long => (
                    floor_to_tick(calc_level(state, params.long_tp_pct), tick),
                    round_to_tick(calc_level(state, -0.23), tick),
                ),
                Direction::Short => (
                    ceil_to_tick(calc_level(state, params.short_tp_pct), tick),
                    round_to_tick(calc_level(state, 1.23), tick),
                ),
            };

            state.position.direction = Some(order.direction);
            state.position.legs.push(PositionLeg {
                entry_name: order.signal_name.clone(),
                fill_price,
                tp_price: tp,
                sl_price: sl,
                is_active: true,
            });

            state.stop_order = None;

            // 放置加仓单
            place_addon_orders(state, params);
        }
    }

    // 3. 检查加仓 1
    if let Some(ref order) = state.addon_order.clone() {
        let filled = match order.direction {
            Direction::Long => bar.low <= order.price,
            Direction::Short => bar.high >= order.price,
        };

        if filled {
            let fill_price = order.price;
            state.addon1_filled = true;

            let (tp, sl) = match order.direction {
                Direction::Long => (
                    floor_to_tick(calc_level(state, params.long_tp_pct), tick),
                    round_to_tick(calc_level(state, -0.23), tick),
                ),
                Direction::Short => (
                    ceil_to_tick(calc_level(state, params.short_tp_pct), tick),
                    round_to_tick(calc_level(state, 1.23), tick),
                ),
            };

            state.position.legs.push(PositionLeg {
                entry_name: order.signal_name.clone(),
                fill_price,
                tp_price: tp,
                sl_price: sl,
                is_active: true,
            });

            state.addon_order = None;
        }
    }

    // 4. 检查加仓 2
    if let Some(ref order) = state.addon2_order.clone() {
        let filled = match order.direction {
            Direction::Long => bar.low <= order.price,
            Direction::Short => bar.high >= order.price,
        };

        if filled {
            let fill_price = order.price;
            state.addon2_filled = true;

            // 加仓 2 的 TP 是保本价
            let addon1_price = if state.direction == Some(Direction::Long) {
                round_to_tick(calc_level(state, params.long_addon_pct), tick)
            } else {
                round_to_tick(calc_level(state, params.short_addon_pct), tick)
            };
            let est_be =
                round_to_tick((state.stop_fill_price + addon1_price + fill_price) / 3.0, tick);

            let sl = match order.direction {
                Direction::Long => round_to_tick(calc_level(state, -0.23), tick),
                Direction::Short => round_to_tick(calc_level(state, 1.23), tick),
            };

            state.position.legs.push(PositionLeg {
                entry_name: order.signal_name.clone(),
                fill_price,
                tp_price: est_be,
                sl_price: sl,
                is_active: true,
            });

            state.addon2_order = None;

            // 触发保本模式
            state.pending_be_update = true;
        }
    }
}

/// 检查持仓腿的 TP/SL 出场
fn check_position_exits(state: &mut StrategyState, bar: &SecondBar, trades: &mut Vec<Trade>) {
    let dir = match state.position.direction {
        Some(d) => d,
        None => return,
    };

    let mut main_exited_addon1_not_filled = false;

    for i in 0..state.position.legs.len() {
        if !state.position.legs[i].is_active {
            continue;
        }

        // 复制所需数据，避免借用冲突
        let sl_price = state.position.legs[i].sl_price;
        let tp_price = state.position.legs[i].tp_price;
        let fill_price = state.position.legs[i].fill_price;
        let entry_name = state.position.legs[i].entry_name.clone();

        let is_main = (entry_name.contains("MB_BO_Long") || entry_name.contains("MB_BO_Short"))
            && !entry_name.contains("AddOn");
        let is_addon1 =
            (entry_name.contains("AddOn")) && !entry_name.contains("AddOn2");

        // 检查 SL
        let sl_hit = match dir {
            Direction::Long => bar.low <= sl_price,
            Direction::Short => bar.high >= sl_price,
        };

        // 检查 TP
        let tp_hit = match dir {
            Direction::Long => bar.high >= tp_price,
            Direction::Short => bar.low <= tp_price,
        };

        if sl_hit {
            let exit_price = sl_price;
            let pnl = match dir {
                Direction::Long => exit_price - fill_price,
                Direction::Short => fill_price - exit_price,
            };

            trades.push(Trade {
                entry_time: 0,
                exit_time: bar.timestamp,
                direction: format!("{:?}", dir),
                signal_name: entry_name.clone(),
                entry_price: fill_price,
                exit_price,
                pnl,
                exit_reason: "SL".to_string(),
            });

            state.position.legs[i].is_active = false;

            if is_main && !state.addon1_filled {
                main_exited_addon1_not_filled = true;
            }
            if is_addon1 && !state.addon2_filled {
                state.addon2_order = None;
            }
        } else if tp_hit {
            let exit_price = tp_price;
            let pnl = match dir {
                Direction::Long => exit_price - fill_price,
                Direction::Short => fill_price - exit_price,
            };

            trades.push(Trade {
                entry_time: 0,
                exit_time: bar.timestamp,
                direction: format!("{:?}", dir),
                signal_name: entry_name.clone(),
                entry_price: fill_price,
                exit_price,
                pnl,
                exit_reason: "TP".to_string(),
            });

            state.position.legs[i].is_active = false;

            if is_main && !state.addon1_filled {
                main_exited_addon1_not_filled = true;
            }
            if is_addon1 && !state.addon2_filled {
                state.addon2_order = None;
            }
        }
    }

    if main_exited_addon1_not_filled {
        state.addon_order = None;
        state.addon2_order = None;
    }

    if state.state == State::EntryFilled && state.position.is_flat() {
        let mb_id = state.mb_id;
        state.reset();
        state.mb_id = mb_id;
    }
}

/// 时段外处理
fn handle_out_of_session(state: &mut StrategyState, bar: &SecondBar, trades: &mut Vec<Trade>) {
    // 平掉所有持仓
    if let Some(dir) = state.position.direction {
        for leg in state.position.legs.iter_mut() {
            if leg.is_active {
                let pnl = match dir {
                    Direction::Long => bar.close - leg.fill_price,
                    Direction::Short => leg.fill_price - bar.close,
                };
                trades.push(Trade {
                    entry_time: 0,
                    exit_time: bar.timestamp,
                    direction: format!("{:?}", dir),
                    signal_name: leg.entry_name.clone(),
                    entry_price: leg.fill_price,
                    exit_price: bar.close,
                    pnl,
                    exit_reason: "SessionClose".to_string(),
                });
                leg.is_active = false;
            }
        }
    }

    // 取消所有挂单
    state.stop_order = None;
    state.addon_order = None;
    state.addon2_order = None;

    if state.state != State::Idle {
        let mb_id = state.mb_id;
        state.reset();
        state.mb_id = mb_id;
    }
}

// ─── 主回测循环 ───

pub fn run_backtest(
    params: &StrategyParams,
    second_bars: &[SecondBar],
    five_min_bars: &[FiveMinBar],
) -> BacktestResult {
    let mut state = StrategyState::new();
    let mut trades: Vec<Trade> = Vec::new();

    if five_min_bars.len() < 3 {
        return empty_result(params);
    }

    // 遍历 5m bar（从 index 2 开始，需要前一根 bar 做 inside bar 检测）
    for bar_idx in 2..five_min_bars.len() {
        let bar_5m = &five_min_bars[bar_idx];
        let prev_5m = &five_min_bars[bar_idx - 1];

        // 处理该 5m bar 内的所有秒级 bar
        for sec_idx in bar_5m.second_bar_start..bar_5m.second_bar_end {
            let sec_bar = &second_bars[sec_idx];

            // 1. 检查订单匹配（每个秒级 bar）
            if state.state == State::StopPending || state.state == State::EntryFilled {
                check_order_fills(&mut state, sec_bar, params, &mut trades);
            }

            // 2. 检查 pending BE 更新
            if state.pending_be_update && state.state == State::EntryFilled {
                update_all_tps_to_breakeven(&mut state, params.tick_size);
            }
        }

        // 3. 5m bar 收盘后执行状态机逻辑（IsFirstTickOfBar）
        let in_session = is_in_trade_session(bar_5m.timestamp, params);

        if !in_session {
            // 用 5m bar 的最后一个秒级 bar 作为平仓价格参考
            if bar_5m.second_bar_end > bar_5m.second_bar_start {
                let last_sec = &second_bars[bar_5m.second_bar_end - 1];
                handle_out_of_session(&mut state, last_sec, &mut trades);
            }
            continue;
        }

        match state.state {
            State::Idle => {
                detect_mother_bar(&mut state, bar_5m, prev_5m, bar_idx, params);
            }
            State::WaitingConfirmation => {
                check_larger_mb_replacement(&mut state, bar_5m, prev_5m, bar_idx, params);
                if state.state == State::WaitingConfirmation {
                    check_invalidation(&mut state, bar_5m);
                }
                if state.state == State::WaitingConfirmation {
                    check_confirmation_signal(&mut state, bar_5m, params);
                }
            }
            State::StopPending => {
                check_larger_mb_replacement(&mut state, bar_5m, prev_5m, bar_idx, params);
                if state.state == State::StopPending {
                    check_invalidation(&mut state, bar_5m);
                }
                if state.state == State::StopPending {
                    check_direction_flip(&mut state, bar_5m, params);
                }
            }
            State::EntryFilled => {
                // TP/SL 在秒级循环中已处理
            }
        }
    }

    // 计算统计指标
    compute_result(params, trades)
}

fn compute_result(params: &StrategyParams, trades: Vec<Trade>) -> BacktestResult {
    let net_profit: f64 = trades.iter().map(|t| t.pnl).sum();
    let trade_count = trades.len();
    let win_count = trades.iter().filter(|t| t.pnl > 0.0).count();
    let win_rate = if trade_count > 0 {
        win_count as f64 / trade_count as f64
    } else {
        0.0
    };

    // Max Drawdown
    let mut equity = 0.0_f64;
    let mut peak = 0.0_f64;
    let mut max_dd = 0.0_f64;
    for t in &trades {
        equity += t.pnl;
        if equity > peak {
            peak = equity;
        }
        let dd = peak - equity;
        if dd > max_dd {
            max_dd = dd;
        }
    }

    // Profit Factor
    let gross_profit: f64 = trades.iter().filter(|t| t.pnl > 0.0).map(|t| t.pnl).sum();
    let gross_loss: f64 = trades
        .iter()
        .filter(|t| t.pnl < 0.0)
        .map(|t| t.pnl.abs())
        .sum();
    let profit_factor = if gross_loss > 0.0 {
        gross_profit / gross_loss
    } else if gross_profit > 0.0 {
        f64::INFINITY
    } else {
        0.0
    };

    // Sharpe Ratio (简化版，基于每笔交易的 PnL)
    let sharpe_ratio = if trade_count > 1 {
        let pnls: Vec<f64> = trades.iter().map(|t| t.pnl).collect();
        let mean = net_profit / trade_count as f64;
        let variance =
            pnls.iter().map(|p| (p - mean).powi(2)).sum::<f64>() / (trade_count - 1) as f64;
        let std_dev = variance.sqrt();
        if std_dev > 0.0 {
            mean / std_dev
        } else {
            0.0
        }
    } else {
        0.0
    };

    BacktestResult {
        params: params.clone(),
        trades,
        net_profit,
        max_drawdown: max_dd,
        win_rate,
        trade_count,
        profit_factor,
        sharpe_ratio,
    }
}

fn empty_result(params: &StrategyParams) -> BacktestResult {
    BacktestResult {
        params: params.clone(),
        trades: Vec::new(),
        net_profit: 0.0,
        max_drawdown: 0.0,
        win_rate: 0.0,
        trade_count: 0,
        profit_factor: 0.0,
        sharpe_ratio: 0.0,
    }
}

// ─── 并行参数 Sweep ───

pub fn run_parameter_sweep(
    second_bars: &[SecondBar],
    five_min_bars: &[FiveMinBar],
    param_grid: &[StrategyParams],
) -> Vec<BacktestResult> {
    use rayon::prelude::*;

    param_grid
        .par_iter()
        .map(|params| run_backtest(params, second_bars, five_min_bars))
        .collect()
}
