# 最佳配置语言配置
best_config = {
    "ZH_CN": {
        "title": {"name": "最佳配置", "description": "一键优化点名抽取的公平性设置"},
        "recommend_btn": {
            "name": "一键推荐配置",
            "description": "将点名设置一键更新为推荐配置",
        },
        "reset_defaults_btn": {
            "name": "恢复默认公平抽取设置",
            "description": "将所有公平抽取设置恢复为默认值",
        },
        "reset_confirm_title": {
            "name": "确认恢复默认设置",
            "description": "恢复默认确认对话框标题",
        },
        "reset_confirm_content": {
            "name": "将重置所有公平抽取设置为默认值，是否继续？",
            "description": "恢复默认确认对话框内容",
        },
        "analyze_btn": {
            "name": "智能分析优化",
            "description": "根据历史记录智能分析并推荐优化（需要至少 200 次以上点名记录才可用）",
        },
        "analyze_disabled_hint": {
            "name": "需要至少 200 次以上点名记录才可用",
            "description": "按钮2灰显时的提示文字",
        },
        "confirm_title": {
            "name": "确认应用推荐配置",
            "description": "确认对话框标题",
        },
        "confirm_content_template": {
            "name": "将应用以下配置变更:\n{changes}\n\n是否继续?",
            "description": "确认对话框内容模板",
        },
        "apply_success": {
            "name": "配置已更新",
            "description": "应用成功后提示",
        },
        "undo_btn": {
            "name": "撤销变更",
            "description": "撤销按钮文字",
        },
        "undo_success": {
            "name": "已恢复变更前的配置",
            "description": "撤销成功后提示",
        },
        "analysis_window_title": {
            "name": "智能分析 - 最佳配置建议",
            "description": "分析窗口标题",
        },
        "stats_section_title": {
            "name": "历史统计",
            "description": "统计区域标题",
        },
        "total_draws_label": {
            "name": "总点名次数",
            "description": "总点名次数标签",
        },
        "current_config_label": {
            "name": "当前配置",
            "description": "当前配置标签",
        },
        "suggestions_section_title": {
            "name": "优化建议",
            "description": "建议区域标题",
        },
        "select_all_btn": {
            "name": "全选",
            "description": "全选按钮",
        },
        "deselect_all_btn": {
            "name": "取消全选",
            "description": "取消全选按钮",
        },
        "apply_selected_btn": {
            "name": "应用所选变更",
            "description": "应用按钮",
        },
        "no_recommendations": {
            "name": "当前配置已是最优，无需优化。",
            "description": "无需优化时提示",
        },
        "reason_draw_type_fair": {
            "name": "随机抽取可能导致重复，推荐切换为公平抽取模式",
            "description": "推荐将抽取类型改为公平抽取的理由",
        },
        "reason_enable_avg_gap": {
            "name": "开启平均值过滤可避免过度抽取某些成员",
            "description": "推荐开启平均值保护的理由",
        },
        "reason_fair_draw_time": {
            "name": "将距上次抽取时间纳入权重计算，使长期未抽中者获得更高概率",
            "description": "推荐开启时间权重因子的理由",
        },
        "reason_cold_start": {
            "name": "冷启动保护确保新成员或长期未被抽中的成员不会因权重过低而失去机会",
            "description": "推荐开启冷启动保护的理由",
        },
        "reason_shield": {
            "name": "抽取后屏蔽可避免同一人短时间内被重复抽中",
            "description": "推荐开启抽取后屏蔽的理由",
        },
        "reason_transparency": {
            "name": "显示权重透明度可让用户了解每位成员的抽取概率",
            "description": "推荐开启权重透明度的理由",
        },
        "reason_base_weight": {
            "name": "将基础权重恢复为推荐默认值 1.00",
            "description": "推荐恢复基础权重为默认值的理由",
        },
        "reason_min_weight": {
            "name": "将权重最小值恢复为推荐默认值 0.50",
            "description": "推荐恢复权重最小值为默认值的理由",
        },
        "reason_max_weight": {
            "name": "将权重最大值恢复为推荐默认值 5.00",
            "description": "推荐恢复权重最大值为默认值的理由",
        },
        "reason_frequency_function": {
            "name": "将频率函数类型恢复为推荐默认值",
            "description": "推荐恢复频率函数为默认值的理由",
        },
        "reason_frequency_weight": {
            "name": "将频率权重恢复为推荐默认值 1.00",
            "description": "推荐恢复频率权重为默认值的理由",
        },
        "reason_group_weight": {
            "name": "将小组平衡权重恢复为推荐默认值 0.80",
            "description": "推荐恢复小组平衡权重为默认值的理由",
        },
        "reason_gender_weight": {
            "name": "将性别平衡权重恢复为推荐默认值 0.80",
            "description": "推荐恢复性别平衡权重为默认值的理由",
        },
        "reason_time_weight": {
            "name": "将时间因子权重恢复为推荐默认值 0.50",
            "description": "推荐恢复时间因子权重为默认值的理由",
        },
        "reason_cold_start_rounds": {
            "name": "将冷启动轮次恢复为推荐默认值 10",
            "description": "推荐恢复冷启动轮次为默认值的理由",
        },
        "reason_gap_threshold": {
            "name": "将差值阈值恢复为推荐默认值 1",
            "description": "推荐恢复差值阈值为默认值的理由",
        },
        "reason_min_pool_size": {
            "name": "将候选池最少人数恢复为推荐默认值 5",
            "description": "推荐恢复候选池人数为默认值的理由",
        },
        "reason_shield_time": {
            "name": "将屏蔽时间恢复为推荐默认值 5.0 秒",
            "description": "推荐恢复屏蔽时间为默认值的理由",
        },
        "reason_shield_time_unit": {
            "name": "将屏蔽时间单位恢复为推荐默认值",
            "description": "推荐恢复屏蔽时间单位为默认值的理由",
        },
        "confirm_btn": {"name": "确认", "description": "确认按钮文字"},
        "cancel_btn": {"name": "取消", "description": "取消按钮文字"},
    },
    "JA": {
        "title": {
            "name": "最適構成",
            "description": "ワンクリックで抽選の公平性設定を最適化",
        },
        "recommend_btn": {
            "name": "おすすめ構成を適用",
            "description": "点名設定を推奨構成にワンクリックで更新",
        },
        "reset_defaults_btn": {
            "name": "デフォルトの公平抽選設定に戻す",
            "description": "すべての公平抽選設定をデフォルトに復元",
        },
        "reset_confirm_title": {
            "name": "デフォルト設定の復元確認",
            "description": "復元確認ダイアログのタイトル",
        },
        "reset_confirm_content": {
            "name": "すべての公平抽選設定をデフォルトに復元します。続行しますか？",
            "description": "復元確認ダイアログの内容",
        },
        "analyze_btn": {
            "name": "スマート分析の最適化",
            "description": "履歴に基づいてスマート分析し最適化を推奨（少なくとも200回以上の点名記録が必要です）",
        },
        "analyze_disabled_hint": {
            "name": "少なくとも200回以上の点名記録が必要です",
            "description": "ボタン2が無効時のヒント",
        },
        "confirm_title": {
            "name": "推奨構成の適用確認",
            "description": "確認ダイアログのタイトル",
        },
        "confirm_content_template": {
            "name": "以下の設定変更を適用します:\n{changes}\n\n続行しますか?",
            "description": "確認ダイアログの内容テンプレート",
        },
        "apply_success": {
            "name": "設定が更新されました",
            "description": "適用成功時の通知",
        },
        "undo_btn": {
            "name": "変更を元に戻す",
            "description": "取り消しボタンのテキスト",
        },
        "undo_success": {
            "name": "設定を変更前に復元しました",
            "description": "取り消し成功時の通知",
        },
        "analysis_window_title": {
            "name": "スマート分析 - 最適構成の提案",
            "description": "分析ウィンドウのタイトル",
        },
        "stats_section_title": {
            "name": "履歴統計",
            "description": "統計セクションのタイトル",
        },
        "total_draws_label": {
            "name": "総点名回数",
            "description": "総点名回数ラベル",
        },
        "current_config_label": {
            "name": "現在の設定",
            "description": "現在の設定ラベル",
        },
        "suggestions_section_title": {
            "name": "最適化の提案",
            "description": "提案セクションのタイトル",
        },
        "select_all_btn": {
            "name": "すべて選択",
            "description": "全選択ボタン",
        },
        "deselect_all_btn": {
            "name": "選択解除",
            "description": "選択解除ボタン",
        },
        "apply_selected_btn": {
            "name": "選択した変更を適用",
            "description": "適用ボタン",
        },
        "no_recommendations": {
            "name": "現在の設定はすでに最適です。変更は必要ありません。",
            "description": "推奨なし時の表示",
        },
        "reason_draw_type_fair": {
            "name": "ランダム抽選は重複の原因になる可能性があります。公平抽選モードへの切替を推奨します。",
            "description": "公平抽選タイプを推奨する理由",
        },
        "reason_enable_avg_gap": {
            "name": "平均値フィルターを有効にすると、特定のメンバーが過剰に抽選されるのを防ぎます。",
            "description": "平均値保護を推奨する理由",
        },
        "reason_fair_draw_time": {
            "name": "前回抽選からの経過時間を重みに組み込み、長期間抽選されていない人に高い確率を与えます。",
            "description": "時間重み係数を推奨する理由",
        },
        "reason_cold_start": {
            "name": "コールドスタート保護により、新規メンバーや長期間未抽選のメンバーが低重みで機会を失うのを防ぎます。",
            "description": "コールドスタート保護を推奨する理由",
        },
        "reason_shield": {
            "name": "抽選後シールドにより、同じ人が短時間で繰り返し抽選されるのを防ぎます。",
            "description": "抽選後シールドを推奨する理由",
        },
        "reason_transparency": {
            "name": "重みの透明性を表示することで、各メンバーの抽選確率をユーザーが確認できます。",
            "description": "重み透明性を推奨する理由",
        },
        "reason_base_weight": {
            "name": "基礎重みを推奨デフォルト値 1.00 に戻す",
            "description": "",
        },
        "reason_min_weight": {
            "name": "最小重みを推奨デフォルト値 0.50 に戻す",
            "description": "",
        },
        "reason_max_weight": {
            "name": "最大重みを推奨デフォルト値 5.00 に戻す",
            "description": "",
        },
        "reason_frequency_function": {
            "name": "頻度関数を推奨デフォルト値に戻す",
            "description": "",
        },
        "reason_frequency_weight": {
            "name": "頻度重みを推奨デフォルト値 1.00 に戻す",
            "description": "",
        },
        "reason_group_weight": {
            "name": "グループバランス重みを推奨デフォルト値 0.80 に戻す",
            "description": "",
        },
        "reason_gender_weight": {
            "name": "性別バランス重みを推奨デフォルト値 0.80 に戻す",
            "description": "",
        },
        "reason_time_weight": {
            "name": "時間重みを推奨デフォルト値 0.50 に戻す",
            "description": "",
        },
        "reason_cold_start_rounds": {
            "name": "コールドスタート回数を推奨デフォルト値 10 に戻す",
            "description": "",
        },
        "reason_gap_threshold": {
            "name": "差のしきい値を推奨デフォルト値 1 に戻す",
            "description": "",
        },
        "reason_min_pool_size": {
            "name": "候補プール最小人数を推奨デフォルト値 5 に戻す",
            "description": "",
        },
        "reason_shield_time": {
            "name": "シールド時間を推奨デフォルト値 5.0 秒に戻す",
            "description": "",
        },
        "reason_shield_time_unit": {
            "name": "シールド時間単位を推奨デフォルト値に戻す",
            "description": "",
        },
        "confirm_btn": {"name": "確認", "description": "確認ボタンのテキスト"},
        "cancel_btn": {
            "name": "キャンセル",
            "description": "キャンセルボタンのテキスト",
        },
    },
    "EN": {
        "title": {
            "name": "Best Configuration",
            "description": "One-click optimization for roll call fairness settings",
        },
        "recommend_btn": {
            "name": "One-Click Recommended Config",
            "description": "Apply recommended settings with one click",
        },
        "reset_defaults_btn": {
            "name": "Reset to Default Fair Draw Settings",
            "description": "Reset all fair draw settings to defaults",
        },
        "reset_confirm_title": {
            "name": "Confirm Reset Defaults",
            "description": "Reset confirmation dialog title",
        },
        "reset_confirm_content": {
            "name": "All fair draw settings will be reset to defaults. Continue?",
            "description": "Reset confirmation dialog content",
        },
        "analyze_btn": {
            "name": "Smart Analysis & Optimization",
            "description": "Analyze history and recommend optimal settings (requires at least 200 roll call records)",
        },
        "analyze_disabled_hint": {
            "name": "Requires at least 200 roll call records",
            "description": "Hint shown when button 2 is disabled",
        },
        "confirm_title": {
            "name": "Confirm Apply Recommended Config",
            "description": "Confirmation dialog title",
        },
        "confirm_content_template": {
            "name": "The following changes will be applied:\n{changes}\n\nContinue?",
            "description": "Confirmation dialog content template",
        },
        "apply_success": {
            "name": "Configuration updated",
            "description": "Success notification after applying",
        },
        "undo_btn": {
            "name": "Undo Changes",
            "description": "Undo button text",
        },
        "undo_success": {
            "name": "Configuration restored to previous state",
            "description": "Undo success notification",
        },
        "analysis_window_title": {
            "name": "Smart Analysis - Best Configuration Suggestions",
            "description": "Analysis window title",
        },
        "stats_section_title": {
            "name": "History Statistics",
            "description": "Statistics section title",
        },
        "total_draws_label": {
            "name": "Total Roll Call Count",
            "description": "Total draws label",
        },
        "current_config_label": {
            "name": "Current Configuration",
            "description": "Current config label",
        },
        "suggestions_section_title": {
            "name": "Optimization Suggestions",
            "description": "Suggestions section title",
        },
        "select_all_btn": {
            "name": "Select All",
            "description": "Select all button",
        },
        "deselect_all_btn": {
            "name": "Deselect All",
            "description": "Deselect all button",
        },
        "apply_selected_btn": {
            "name": "Apply Selected Changes",
            "description": "Apply button",
        },
        "no_recommendations": {
            "name": "Current configuration is already optimal. No changes needed.",
            "description": "No recommendations hint",
        },
        "reason_draw_type_fair": {
            "name": "Random draw may cause repeats. Recommend switching to fair extraction mode.",
            "description": "Reason for recommending fair draw type",
        },
        "reason_enable_avg_gap": {
            "name": "Enable average gap protection to prevent over-drawing certain members.",
            "description": "Reason for recommending average gap protection",
        },
        "reason_fair_draw_time": {
            "name": "Factor time since last draw into weights, giving higher probability to those drawn least recently.",
            "description": "Reason for recommending time-based weight",
        },
        "reason_cold_start": {
            "name": "Cold start protection ensures new or long-unpicked members won't lose chances due to low weight.",
            "description": "Reason for recommending cold start protection",
        },
        "reason_shield": {
            "name": "Post-draw shield prevents the same person from being drawn repeatedly in a short time.",
            "description": "Reason for recommending post-draw shield",
        },
        "reason_transparency": {
            "name": "Show weight transparency to let users see each member's draw probability.",
            "description": "Reason for recommending weight transparency",
        },
        "reason_base_weight": {
            "name": "Restore base weight to recommended default 1.00",
            "description": "",
        },
        "reason_min_weight": {
            "name": "Restore min weight to recommended default 0.50",
            "description": "",
        },
        "reason_max_weight": {
            "name": "Restore max weight to recommended default 5.00",
            "description": "",
        },
        "reason_frequency_function": {
            "name": "Restore frequency function to recommended default",
            "description": "",
        },
        "reason_frequency_weight": {
            "name": "Restore frequency weight to recommended default 1.00",
            "description": "",
        },
        "reason_group_weight": {
            "name": "Restore group weight to recommended default 0.80",
            "description": "",
        },
        "reason_gender_weight": {
            "name": "Restore gender weight to recommended default 0.80",
            "description": "",
        },
        "reason_time_weight": {
            "name": "Restore time weight to recommended default 0.50",
            "description": "",
        },
        "reason_cold_start_rounds": {
            "name": "Restore cold start rounds to recommended default 10",
            "description": "",
        },
        "reason_gap_threshold": {
            "name": "Restore gap threshold to recommended default 1",
            "description": "",
        },
        "reason_min_pool_size": {
            "name": "Restore min pool size to recommended default 5",
            "description": "",
        },
        "reason_shield_time": {
            "name": "Restore shield time to recommended default 5.0 seconds",
            "description": "",
        },
        "reason_shield_time_unit": {
            "name": "Restore shield time unit to recommended default",
            "description": "",
        },
        "confirm_btn": {"name": "Confirm", "description": "Confirm button text"},
        "cancel_btn": {"name": "Cancel", "description": "Cancel button text"},
    },
}
