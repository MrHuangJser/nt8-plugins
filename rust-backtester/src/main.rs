mod data;
mod engine;
mod types;

use clap::Parser;
use std::path::PathBuf;
use std::time::Instant;

#[derive(Parser, Debug)]
#[command(name = "mb-backtester")]
#[command(about = "MotherBarBreakout Rust 高性能回测引擎")]
struct Args {
    /// NT8 导出的秒级数据文件路径 (.txt, 分号分隔)
    #[arg(short, long)]
    data: PathBuf,

    /// 输出 CSV 路径 (交易列表)
    #[arg(short, long, default_value = "trades.csv")]
    output: PathBuf,

    /// 输出汇总 CSV (参数 sweep 结果)
    #[arg(short, long, default_value = "summary.csv")]
    summary: PathBuf,

    /// Tick size (默认 0.25 for ES)
    #[arg(long, default_value = "0.25")]
    tick_size: f64,

    /// 是否运行参数 sweep
    #[arg(long)]
    sweep: bool,
}

fn main() {
    let args = Args::parse();

    // 1. 加载数据
    println!("=== MotherBarBreakout Rust Backtester ===");
    let start = Instant::now();

    let second_bars = data::load_second_bars(&args.data).expect("Failed to load data");
    let five_min_bars = data::aggregate_to_5min(&second_bars);

    println!("Data loaded in {:.2}s", start.elapsed().as_secs_f64());

    if args.sweep {
        run_sweep(&second_bars, &five_min_bars, &args);
    } else {
        run_single(&second_bars, &five_min_bars, &args);
    }
}

fn run_single(
    second_bars: &[types::SecondBar],
    five_min_bars: &[types::FiveMinBar],
    args: &Args,
) {
    let mut params = types::StrategyParams::default_es();
    params.tick_size = args.tick_size;

    let start = Instant::now();
    let result = engine::run_backtest(&params, second_bars, five_min_bars);
    let elapsed = start.elapsed();

    println!("\n=== 回测结果 ===");
    println!("交易次数: {}", result.trade_count);
    println!("净利润:   {:.2}", result.net_profit);
    println!("最大回撤: {:.2}", result.max_drawdown);
    println!("胜率:     {:.2}%", result.win_rate * 100.0);
    println!("盈亏比:   {:.3}", result.profit_factor);
    println!("Sharpe:   {:.3}", result.sharpe_ratio);
    println!("耗时:     {:.3}ms", elapsed.as_secs_f64() * 1000.0);

    write_trades_csv(&result.trades, &args.output).expect("Failed to write trades CSV");
    println!("\n交易列表已输出到: {:?}", args.output);
}

fn run_sweep(
    second_bars: &[types::SecondBar],
    five_min_bars: &[types::FiveMinBar],
    args: &Args,
) {
    let param_grid = generate_param_grid(args.tick_size);
    println!("参数组合数: {}", param_grid.len());

    let start = Instant::now();
    let results = engine::run_parameter_sweep(second_bars, five_min_bars, &param_grid);
    let elapsed = start.elapsed();

    println!(
        "\n=== 参数 Sweep 完成 ===\n耗时: {:.2}s\n",
        elapsed.as_secs_f64()
    );

    write_summary_csv(&results, &args.summary).expect("Failed to write summary CSV");
    println!("汇总结果已输出到: {:?}", args.summary);

    // Top 10
    let mut sorted = results.clone();
    sorted.sort_by(|a, b| b.net_profit.partial_cmp(&a.net_profit).unwrap());
    println!(
        "\n=== Top 10 参数组合 ===\n{:>4} {:>10} {:>10} {:>8} {:>8} {:>8}",
        "#", "NetProfit", "MaxDD", "WinRate", "PF", "Trades"
    );
    for (i, r) in sorted.iter().take(10).enumerate() {
        println!(
            "{:>4} {:>10.2} {:>10.2} {:>7.1}% {:>8.2} {:>8}",
            i + 1,
            r.net_profit,
            r.max_drawdown,
            r.win_rate * 100.0,
            r.profit_factor,
            r.trade_count,
        );
    }
}

fn generate_param_grid(tick_size: f64) -> Vec<types::StrategyParams> {
    let mut grid = Vec::new();

    let long_tp_range = [1.0, 1.236, 1.382, 1.5, 1.618, 2.0, 2.618];
    let short_tp_range = [-0.382, -0.5, -0.618, -1.0, -1.618];
    let long_addon_range = [0.618, 0.66, 0.725, 0.79, 0.89];
    let short_addon_range = [0.11, 0.21, 0.27, 0.33, 0.382];

    for &long_tp in &long_tp_range {
        for &short_tp in &short_tp_range {
            for &long_addon in &long_addon_range {
                for &short_addon in &short_addon_range {
                    let mut params = types::StrategyParams::default_es();
                    params.tick_size = tick_size;
                    params.long_tp_pct = long_tp;
                    params.short_tp_pct = short_tp;
                    params.long_addon_pct = long_addon;
                    params.short_addon_pct = short_addon;
                    params.long_addon2_pct = short_addon;
                    params.short_addon2_pct = long_addon;
                    grid.push(params);
                }
            }
        }
    }

    grid
}

fn write_trades_csv(
    trades: &[types::Trade],
    path: &std::path::Path,
) -> Result<(), Box<dyn std::error::Error>> {
    let mut wtr = csv::Writer::from_path(path)?;

    wtr.write_record([
        "exit_time",
        "direction",
        "signal_name",
        "entry_price",
        "exit_price",
        "pnl",
        "exit_reason",
    ])?;

    for t in trades {
        let dt = chrono::DateTime::from_timestamp(t.exit_time, 0)
            .map(|d| d.format("%Y-%m-%d %H:%M:%S").to_string())
            .unwrap_or_default();

        wtr.write_record([
            &dt,
            &t.direction,
            &t.signal_name,
            &format!("{:.2}", t.entry_price),
            &format!("{:.2}", t.exit_price),
            &format!("{:.2}", t.pnl),
            &t.exit_reason,
        ])?;
    }

    wtr.flush()?;
    Ok(())
}

fn write_summary_csv(
    results: &[types::BacktestResult],
    path: &std::path::Path,
) -> Result<(), Box<dyn std::error::Error>> {
    let mut wtr = csv::Writer::from_path(path)?;

    wtr.write_record([
        "long_tp_pct",
        "short_tp_pct",
        "long_addon_pct",
        "short_addon_pct",
        "net_profit",
        "max_drawdown",
        "win_rate",
        "trade_count",
        "profit_factor",
        "sharpe_ratio",
    ])?;

    for r in results {
        wtr.write_record([
            &format!("{:.3}", r.params.long_tp_pct),
            &format!("{:.3}", r.params.short_tp_pct),
            &format!("{:.3}", r.params.long_addon_pct),
            &format!("{:.3}", r.params.short_addon_pct),
            &format!("{:.2}", r.net_profit),
            &format!("{:.2}", r.max_drawdown),
            &format!("{:.4}", r.win_rate),
            &r.trade_count.to_string(),
            &format!("{:.3}", r.profit_factor),
            &format!("{:.3}", r.sharpe_ratio),
        ])?;
    }

    wtr.flush()?;
    Ok(())
}
