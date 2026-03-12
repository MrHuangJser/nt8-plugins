#!/usr/bin/env python3
"""Generate markdown report from deep research JSON results."""

import json
import os
import glob
import re
import yaml

TOPIC_DIR = os.path.dirname(os.path.abspath(__file__))
RESULTS_DIR = os.path.join(TOPIC_DIR, "results")
FIELDS_FILE = os.path.join(TOPIC_DIR, "fields.yaml")
OUTPUT_FILE = os.path.join(TOPIC_DIR, "report.md")

SUMMARY_FIELDS = ["feasibility", "complexity", "category", "latency"]

CATEGORY_MAPPING = {
    "basic_info": ["basic_info", "基本信息"],
    "feasibility": ["feasibility", "可行性"],
    "performance": ["performance", "性能指标", "performance_metrics"],
    "data_quality": ["data_quality", "数据质量"],
    "integration": ["integration", "集成", "integration_method"],
    "ecosystem": ["ecosystem", "生态", "生态系统"],
    "architecture": ["architecture", "架构"],
}

INTERNAL_FIELDS = {"_source_file", "uncertain"}
CATEGORY_KEYS = set()
for aliases in CATEGORY_MAPPING.values():
    CATEGORY_KEYS.update(aliases)


def load_fields():
    with open(FIELDS_FILE, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)
    fields_by_category = {}
    for cat_name, field_list in data.get("fields", {}).items():
        fields_by_category[cat_name] = field_list
    return fields_by_category


def find_field_value(data, field_name):
    """Find field value in flat or nested JSON structure."""
    # Top-level
    if field_name in data:
        return data[field_name]
    # Search nested dicts
    for k, v in data.items():
        if isinstance(v, dict) and field_name in v:
            return v[field_name]
    return None


def is_uncertain(value, field_name, uncertain_list):
    """Check if a field value should be skipped."""
    if value is None or value == "":
        return True
    if field_name in uncertain_list:
        return True
    if isinstance(value, str) and "[不确定]" in value:
        return True
    return False


def format_value(value):
    """Format complex values for markdown display."""
    if isinstance(value, list):
        if not value:
            return ""
        if isinstance(value[0], dict):
            lines = []
            for item in value:
                parts = [f"{k}: {v}" for k, v in item.items()]
                lines.append("  - " + " | ".join(parts))
            return "\n" + "\n".join(lines)
        if len(value) <= 5 and all(isinstance(v, str) and len(v) < 30 for v in value):
            return "、".join(str(v) for v in value)
        lines = ["  - " + str(v) for v in value]
        return "\n" + "\n".join(lines)
    if isinstance(value, dict):
        parts = [f"{k}: {v}" for k, v in value.items()]
        return "；".join(parts)
    val_str = str(value)
    # Break long text for readability
    if len(val_str) > 150:
        val_str = val_str.replace("。", "。<br>")
    return val_str


def make_anchor(name):
    """Create markdown anchor from item name."""
    anchor = name.lower().strip()
    anchor = re.sub(r'[^\w\s\u4e00-\u9fff-]', '', anchor)
    anchor = re.sub(r'\s+', '-', anchor)
    return anchor


def get_summary_value(data, field_name):
    """Get a short summary value for TOC display."""
    val = find_field_value(data, field_name)
    if val is None or (isinstance(val, str) and "[不确定]" in val):
        return "-"
    # If value is a dict (nested category), look for the field inside it
    if isinstance(val, dict):
        if field_name in val:
            val = val[field_name]
        else:
            # Try first value that's a simple string
            for v in val.values():
                if isinstance(v, str) and len(v) < 50:
                    val = v
                    break
            else:
                return "-"
    val_str = str(val)
    if len(val_str) > 20:
        val_str = val_str[:20] + "…"
    return val_str


def main():
    fields_by_category = load_fields()

    # Load all JSON results
    items = []
    for fp in sorted(glob.glob(os.path.join(RESULTS_DIR, "*.json"))):
        with open(fp, "r", encoding="utf-8") as f:
            data = json.load(f)
        name = find_field_value(data, "name") or os.path.splitext(os.path.basename(fp))[0]
        items.append({"name": name, "data": data, "file": os.path.basename(fp)})

    # Group by category
    category_order = ["data_acquisition", "backtest_engine", "result_analysis", "communication"]
    category_labels = {
        "data_acquisition": "数据获取方案",
        "backtest_engine": "Rust 回测框架/引擎",
        "result_analysis": "结果回传与分析",
        "communication": "跨语言通信方案",
    }

    grouped = {c: [] for c in category_order}
    for item in items:
        cat = find_field_value(item["data"], "category") or "other"
        if cat in grouped:
            grouped[cat].append(item)
        else:
            grouped.setdefault("other", []).append(item)

    lines = []
    lines.append("# 使用 Rust 构建高性能回测引擎：技术方案深度调研报告\n")
    lines.append("> 自动生成于深度调研结果，覆盖 NT8 数据获取、Rust 回测引擎、结果分析、跨语言通信四大领域。\n")

    # === Table of Contents ===
    lines.append("## 目录\n")
    idx = 0
    for cat in category_order:
        cat_items = grouped.get(cat, [])
        if not cat_items:
            continue
        lines.append(f"### {category_labels.get(cat, cat)}\n")
        lines.append("| # | 方案 | 可行性 | 复杂度 | 延迟 |")
        lines.append("|---|------|--------|--------|------|")
        for item in cat_items:
            idx += 1
            anchor = make_anchor(item["name"])
            feasibility = get_summary_value(item["data"], "feasibility")
            complexity = get_summary_value(item["data"], "complexity")
            latency = get_summary_value(item["data"], "latency")
            lines.append(f"| {idx} | [{item['name']}](#{anchor}) | {feasibility} | {complexity} | {latency} |")
        lines.append("")

    # === Detailed Content ===
    lines.append("---\n")
    lines.append("## 详细调研结果\n")

    idx = 0
    for cat in category_order:
        cat_items = grouped.get(cat, [])
        if not cat_items:
            continue
        lines.append(f"### {category_labels.get(cat, cat)}\n")

        for item in cat_items:
            idx += 1
            data = item["data"]
            uncertain_list = data.get("uncertain", [])

            lines.append(f"#### {idx}. {item['name']}\n")

            # Process each field category
            tracked_fields = set()
            for cat_name, field_defs in fields_by_category.items():
                cat_fields_output = []
                for field_def in field_defs:
                    fname = field_def["name"]
                    fdesc = field_def.get("description", "")
                    tracked_fields.add(fname)

                    val = find_field_value(data, fname)
                    if is_uncertain(val, fname, uncertain_list):
                        continue

                    formatted = format_value(val)
                    cat_fields_output.append(f"- **{fdesc}**：{formatted}")

                if cat_fields_output:
                    lines.append(f"**{cat_name}**\n")
                    lines.extend(cat_fields_output)
                    lines.append("")

            # Collect extra fields not in fields.yaml
            extra_fields = []
            def collect_extra(d):
                for k, v in d.items():
                    if k in INTERNAL_FIELDS or k in CATEGORY_KEYS or k in tracked_fields:
                        continue
                    if isinstance(v, dict):
                        collect_extra(v)
                    else:
                        if not is_uncertain(v, k, uncertain_list):
                            extra_fields.append((k, v))
            collect_extra(data)

            if extra_fields:
                lines.append("**其他信息**\n")
                for k, v in extra_fields:
                    lines.append(f"- **{k}**：{format_value(v)}")
                lines.append("")

            # Uncertain fields
            if uncertain_list:
                lines.append(f"**不确定字段**：{'、'.join(uncertain_list)}\n")

            lines.append("---\n")

    report = "\n".join(lines)
    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        f.write(report)
    print(f"Report generated: {OUTPUT_FILE}")
    print(f"Total items: {idx}")


if __name__ == "__main__":
    main()
