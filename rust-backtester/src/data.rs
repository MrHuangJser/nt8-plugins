use crate::types::{FiveMinBar, SecondBar};
use chrono::NaiveDateTime;
use std::path::Path;

/// 解析 NT8 导出的分号分隔 txt 文件
/// 格式: yyyyMMdd HHmmss;open;high;low;close;volume
pub fn load_second_bars(path: &Path) -> Result<Vec<SecondBar>, Box<dyn std::error::Error>> {
    let mut reader = csv::ReaderBuilder::new()
        .delimiter(b';')
        .has_headers(false)
        .from_path(path)?;

    let mut bars = Vec::new();

    for result in reader.records() {
        let record = result?;
        if record.len() < 6 {
            continue;
        }

        let datetime_str = record[0].trim();
        let dt = NaiveDateTime::parse_from_str(datetime_str, "%Y%m%d %H%M%S")?;
        let timestamp = dt.and_utc().timestamp();

        let bar = SecondBar {
            timestamp,
            open: record[1].trim().parse()?,
            high: record[2].trim().parse()?,
            low: record[3].trim().parse()?,
            close: record[4].trim().parse()?,
            volume: record[5].trim().parse().unwrap_or(0),
        };
        bars.push(bar);
    }

    println!("Loaded {} second bars from {:?}", bars.len(), path);
    Ok(bars)
}

/// 将秒级 bar 聚合为 5 分钟 bar
/// 使用 5 分钟边界 (00:00, 00:05, 00:10, ...)
pub fn aggregate_to_5min(second_bars: &[SecondBar]) -> Vec<FiveMinBar> {
    if second_bars.is_empty() {
        return Vec::new();
    }

    let mut five_min_bars = Vec::new();
    let mut group_start = 0;

    // 计算 5 分钟 bucket: timestamp / 300 * 300
    let mut current_bucket = second_bars[0].timestamp / 300;

    for i in 1..second_bars.len() {
        let bucket = second_bars[i].timestamp / 300;
        if bucket != current_bucket {
            // 当前 5m bucket 结束，生成 FiveMinBar
            let bar = build_5min_bar(&second_bars[group_start..i], group_start, i);
            five_min_bars.push(bar);

            group_start = i;
            current_bucket = bucket;
        }
    }

    // 最后一组
    if group_start < second_bars.len() {
        let bar = build_5min_bar(
            &second_bars[group_start..],
            group_start,
            second_bars.len(),
        );
        five_min_bars.push(bar);
    }

    println!(
        "Aggregated {} second bars into {} 5-min bars",
        second_bars.len(),
        five_min_bars.len()
    );
    five_min_bars
}

fn build_5min_bar(bars: &[SecondBar], start_idx: usize, end_idx: usize) -> FiveMinBar {
    let open = bars[0].open;
    let close = bars.last().unwrap().close;
    let high = bars.iter().map(|b| b.high).fold(f64::NEG_INFINITY, f64::max);
    let low = bars.iter().map(|b| b.low).fold(f64::INFINITY, f64::min);
    let volume: u64 = bars.iter().map(|b| b.volume).sum();

    FiveMinBar {
        timestamp: bars[0].timestamp,
        open,
        high,
        low,
        close,
        volume,
        second_bar_start: start_idx,
        second_bar_end: end_idx,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_aggregate_basic() {
        // 创建 10 个秒级 bar，跨两个 5 分钟 bucket
        let mut bars = Vec::new();
        let base_ts = 1700000100; // 某个时间戳，属于 bucket 1700000100/300 = 5666667
        let next_bucket_ts = (base_ts / 300 + 1) * 300; // 下一个 5m 边界

        // 5 个 bar 在第一个 bucket
        for i in 0..5 {
            bars.push(SecondBar {
                timestamp: base_ts + i,
                open: 100.0,
                high: 101.0 + i as f64,
                low: 99.0,
                close: 100.5,
                volume: 10,
            });
        }

        // 5 个 bar 在第二个 bucket
        for i in 0..5 {
            bars.push(SecondBar {
                timestamp: next_bucket_ts + i,
                open: 100.5,
                high: 102.0,
                low: 98.0,
                close: 101.0,
                volume: 20,
            });
        }

        let five_min = aggregate_to_5min(&bars);
        assert_eq!(five_min.len(), 2);
        assert_eq!(five_min[0].second_bar_start, 0);
        assert_eq!(five_min[0].second_bar_end, 5);
        assert_eq!(five_min[1].second_bar_start, 5);
        assert_eq!(five_min[1].second_bar_end, 10);
    }
}
