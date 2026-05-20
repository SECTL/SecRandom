from loguru import logger
from PySide6.QtWidgets import QFileDialog

from app.tools.config import NotificationConfig, NotificationType, show_notification
from app.tools.path_utils import get_path
from app.Language.obtain_language import (
    get_any_position_value_async,
    get_content_name_async,
)


def _get_name_column_index(current_mode: int):
    if current_mode == 0:
        return 1
    elif current_mode == 1:
        return 2
    return None


def export_history_table_data(
    table_widget,
    current_mode: int,
    i18n_domain: str,
    current_name: str,
    parent_widget=None,
):
    if table_widget.rowCount() == 0:
        return

    file_path, selected_filter = QFileDialog.getSaveFileName(
        parent_widget,
        get_any_position_value_async(
            "qfiledialog", i18n_domain, "export_history", "caption", "name"
        ),
        f"{current_name}_{get_content_name_async(f'{i18n_domain}_history_table', 'export_default_filename')}-SecRandom",
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

        headers = []
        for col in range(table_widget.columnCount()):
            header_item = table_widget.horizontalHeaderItem(col)
            headers.append(header_item.text() if header_item else f"列{col}")

        export_data = []
        for row in range(table_widget.rowCount()):
            row_data = {}
            for col in range(table_widget.columnCount()):
                item = table_widget.item(row, col)
                row_data[headers[col]] = item.text() if item else ""
            export_data.append(row_data)

        if export_type == "excel":
            import pandas as pd

            df = pd.DataFrame(export_data)
            df.to_excel(str(target_path), index=False, engine="openpyxl")
        elif export_type == "csv":
            import pandas as pd

            df = pd.DataFrame(export_data)
            df.to_csv(str(target_path), index=False, encoding="utf-8-sig")
        else:
            name_col_idx = _get_name_column_index(current_mode)
            with open(str(target_path), "w", encoding="utf-8") as f:
                for row in range(table_widget.rowCount()):
                    if name_col_idx is not None:
                        item = table_widget.item(row, name_col_idx)
                        f.write(f"{item.text()}\n" if item else "\n")
                    else:
                        for col in range(table_widget.columnCount()):
                            item = table_widget.item(row, col)
                            f.write(f"{item.text()}\t" if item else "\t")
                        f.write("\n")

        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification", i18n_domain, "export", "title", "success", "name"
            ),
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
            ),
            content=(
                get_any_position_value_async(
                    "notification", i18n_domain, "export", "content", "error", "name"
                )
                or ""
            ).format(message=str(e)),
            duration=3000,
        )
        show_notification(NotificationType.ERROR, config, parent=parent_widget)
