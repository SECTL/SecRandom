import csv

from loguru import logger
from PySide6.QtWidgets import QFileDialog

from app.tools.config import NotificationConfig, NotificationType, show_notification
from app.tools.path_utils import get_path
from app.Language.obtain_language import (
    get_any_position_value_async,
    get_content_name_async,
)
from app.common.history.history_reader import (
    get_roll_call_student_list,
    get_roll_call_history_data,
    filter_roll_call_history_by_subject,
    get_roll_call_students_data,
    get_roll_call_session_data,
    get_roll_call_student_stats_data,
    check_class_has_gender_or_group,
    get_lottery_pool_list,
    get_lottery_history_data,
    get_lottery_prizes_data,
    get_lottery_session_data,
    get_lottery_prize_stats_data,
)
from app.common.history.weight_utils import (
    calculate_weight,
    format_weight_for_display,
)


def _get_name_column_index(current_mode: int):
    if current_mode == 0:
        return 1
    elif current_mode == 1:
        return 2
    return None


def _build_roll_call_export_data(
    current_name: str,
    current_mode: int,
    current_subject: str,
    current_student_name: str,
):
    headers = []
    rows = []

    cleaned_students = get_roll_call_student_list(current_name)
    history_data = get_roll_call_history_data(current_name)

    if current_subject:
        history_data = filter_roll_call_history_by_subject(
            history_data, current_subject
        )

    has_gender, has_group = check_class_has_gender_or_group(current_name)
    has_class_record = False

    if current_mode == 0:
        students_data = get_roll_call_students_data(
            cleaned_students, history_data, current_subject
        )
        students_weight_data = calculate_weight(
            students_data, current_name, current_subject
        )
        format_weight, _, _ = format_weight_for_display(
            students_weight_data, "next_weight"
        )

        headers = ["ID", "Name"]
        if has_gender:
            headers.append("Gender")
        if has_group:
            headers.append("Group")
        headers.append("Count")
        headers.append("Weight")

        for i, student in enumerate(students_data):
            row = [
                student.get("id", ""),
                student.get("name", ""),
            ]
            if has_gender:
                row.append(student.get("gender", ""))
            if has_group:
                row.append(student.get("group", ""))
            row.append(
                str(student.get("total_count_str", student.get("total_count", 0)))
            )
            if i < len(students_weight_data):
                row.append(
                    str(format_weight(students_weight_data[i].get("next_weight", "")))
                )
            else:
                row.append("")
            rows.append(row)

    elif current_mode == 1:
        students_data = get_roll_call_session_data(
            cleaned_students, history_data, current_subject
        )
        has_class_record = any(
            student.get("class_name", "") for student in students_data
        )
        format_weight, _, _ = format_weight_for_display(students_data, "weight")

        students_data.sort(key=lambda x: x.get("draw_time", ""), reverse=True)

        headers = ["Time", "ID", "Name"]
        if has_gender:
            headers.append("Gender")
        if has_group:
            headers.append("Group")
        if has_class_record:
            headers.append("Subject")
        headers.append("Weight")

        for student in students_data:
            row = [
                student.get("draw_time", ""),
                student.get("id", ""),
                student.get("name", ""),
            ]
            if has_gender:
                gender = student.get("gender", "")
                row.append(str(gender) if gender else "")
            if has_group:
                group = student.get("group", "")
                row.append(str(group) if group else "")
            if has_class_record:
                class_name = student.get("class_name", "")
                row.append(str(class_name) if class_name else "")
            row.append(str(format_weight(student.get("weight", ""))))
            rows.append(row)

    else:
        students_data = get_roll_call_student_stats_data(
            cleaned_students, history_data, current_student_name, current_subject
        )
        has_class_record = any(
            student.get("class_name", "") for student in students_data
        )
        format_weight, _, _ = format_weight_for_display(students_data, "weight")

        students_data.sort(key=lambda x: x.get("draw_time", ""), reverse=True)

        headers = ["Time", "Method", "Count"]
        if has_gender:
            headers.append("Gender")
        if has_group:
            headers.append("Group")
        if has_class_record:
            headers.append("Subject")
        headers.append("Weight")

        for student in students_data:
            row = [
                student.get("draw_time", ""),
                str(student.get("draw_method", "")),
                str(student.get("draw_people_numbers", 0)),
            ]
            if has_gender:
                draw_gender = student.get("draw_gender", "")
                row.append(draw_gender if draw_gender else "")
            if has_group:
                draw_group = student.get("draw_group", "")
                row.append(draw_group if draw_group else "")
            if has_class_record:
                class_name = student.get("class_name", "")
                row.append(str(class_name) if class_name else "")
            row.append(str(format_weight(student.get("weight", ""))))
            rows.append(row)

    return headers, rows


def _build_lottery_export_data(
    current_name: str,
    current_mode: int,
    current_subject: str,
    current_lottery_name: str,
):
    headers = []
    rows = []

    cleaned_lotterys = get_lottery_pool_list(current_name)
    history_data = get_lottery_history_data(current_name)

    has_class_record = False

    if current_mode == 0:
        lotterys_data = get_lottery_prizes_data(cleaned_lotterys, history_data)
        format_weight, _, _ = format_weight_for_display(lotterys_data, "weight")

        headers = ["ID", "Name", "Count", "Weight"]

        for lottery in lotterys_data:
            row = [
                lottery.get("id", ""),
                lottery.get("name", ""),
                str(lottery.get("total_count_str", lottery.get("total_count", 0))),
                str(format_weight(lottery.get("weight", 0))),
            ]
            rows.append(row)

    elif current_mode == 1:
        lotterys_data = get_lottery_session_data(
            cleaned_lotterys, history_data, current_subject
        )
        has_class_record = any(
            lottery.get("class_name", "") for lottery in lotterys_data
        )
        format_weight, _, _ = format_weight_for_display(lotterys_data, "weight")

        lotterys_data.sort(key=lambda x: x.get("draw_time", ""), reverse=True)

        headers = ["Time", "ID", "Name"]
        if has_class_record:
            headers.append("Subject")
        headers.append("Weight")

        for lottery in lotterys_data:
            row = [
                lottery.get("draw_time", ""),
                lottery.get("id", ""),
                lottery.get("name", ""),
            ]
            if has_class_record:
                class_name = lottery.get("class_name", "")
                row.append(str(class_name) if class_name else "")
            row.append(str(format_weight(lottery.get("weight", 0))))
            rows.append(row)

    else:
        lotterys_data = get_lottery_prize_stats_data(
            cleaned_lotterys, history_data, current_lottery_name, current_subject
        )
        has_class_record = any(
            lottery.get("class_name", "") for lottery in lotterys_data
        )
        format_weight, _, _ = format_weight_for_display(lotterys_data, "weight")

        lotterys_data.sort(key=lambda x: x.get("draw_time", ""), reverse=True)

        headers = ["Time", "Count"]
        if has_class_record:
            headers.append("Subject")
        headers.append("Weight")

        for lottery in lotterys_data:
            row = [
                lottery.get("draw_time", ""),
                str(lottery.get("draw_lottery_numbers", 0)),
            ]
            if has_class_record:
                class_name = lottery.get("class_name", "")
                row.append(str(class_name) if class_name else "")
            row.append(str(format_weight(lottery.get("weight", ""))))
            rows.append(row)

    return headers, rows


def _write_excel_stream(target_path, headers, rows):
    import pandas as pd

    for chunk_start in range(0, len(rows), 1000):
        chunk = rows[chunk_start : chunk_start + 1000]
        if chunk_start == 0:
            df = pd.DataFrame(chunk, columns=headers)
            df.to_excel(
                str(target_path),
                index=False,
                engine="openpyxl",
                startrow=0,
            )
        else:
            from openpyxl import load_workbook

            wb = load_workbook(str(target_path))
            ws = wb.active
            for row_data in chunk:
                ws.append(row_data)
            wb.save(str(target_path))


def _write_csv_stream(target_path, headers, rows):
    with open(str(target_path), "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(headers)
        for row in rows:
            writer.writerow(row)


def _write_txt_stream(target_path, headers, rows, current_mode: int):
    name_col_idx = _get_name_column_index(current_mode)
    with open(str(target_path), "w", encoding="utf-8") as f:
        if name_col_idx is not None:
            for row in rows:
                if name_col_idx < len(row):
                    f.write(f"{row[name_col_idx]}\n")
                else:
                    f.write("\n")
        else:
            for row in rows:
                f.write("\t".join(str(v) for v in row) + "\n")


def export_history_table_data(
    table_widget,
    current_mode: int,
    i18n_domain: str,
    current_name: str,
    parent_widget=None,
    current_subject: str = "",
    current_item_name: str = "",
):
    file_path, selected_filter = QFileDialog.getSaveFileName(
        parent_widget,
        get_any_position_value_async(
            "qfiledialog", i18n_domain, "export_history", "caption", "name"
        ),
        f"{current_name}_{get_content_name_async(f'{i18n_domain}_history_table', 'export_default_filename') or ''}-SecRandom",
        get_any_position_value_async(
            "qfiledialog", i18n_domain, "export_history", "filter", "name"
        ),
    )

    if not file_path:
        return

    export_type = (
        "excel"
        if ".xlsx" in selected_filter
        else "csv"
        if ".csv" in selected_filter
        else "txt"
    )

    if export_type == "excel" and not file_path.endswith(".xlsx"):
        file_path += ".xlsx"
    elif export_type == "csv" and not file_path.endswith(".csv"):
        file_path += ".csv"
    elif export_type == "txt" and not file_path.endswith(".txt"):
        file_path += ".txt"

    try:
        target_path = get_path(file_path)
        target_path.parent.mkdir(parents=True, exist_ok=True)

        if i18n_domain == "roll_call":
            headers, rows = _build_roll_call_export_data(
                current_name,
                current_mode,
                current_subject,
                current_item_name,
            )
        else:
            headers, rows = _build_lottery_export_data(
                current_name,
                current_mode,
                current_subject,
                current_item_name,
            )

        if not rows:
            return

        if export_type == "excel":
            _write_excel_stream(target_path, headers, rows)
        elif export_type == "csv":
            _write_csv_stream(target_path, headers, rows)
        else:
            _write_txt_stream(target_path, headers, rows, current_mode)

        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification", i18n_domain, "export", "title", "success", "name"
            )
            or "",
            content=(
                get_any_position_value_async(
                    "notification", i18n_domain, "export", "content", "success", "name"
                )
                or ""
            ).format(path=file_path),
            duration=3000,
        )
        show_notification(NotificationType.SUCCESS, config, parent=parent_widget)
        logger.info(f"历史记录导出成功: {file_path}")

    except Exception as e:
        logger.error(f"导出历史记录失败: {e}")
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification", i18n_domain, "export", "title", "failure", "name"
            )
            or "",
            content=(
                get_any_position_value_async(
                    "notification", i18n_domain, "export", "content", "error", "name"
                )
                or ""
            ).format(message=str(e)),
            duration=3000,
        )
        show_notification(NotificationType.ERROR, config, parent=parent_widget)
