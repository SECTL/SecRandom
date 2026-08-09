# 本地化约定

## 基本结构

- 每个页面的本地化拆分到独立文件夹：
    - 必需：`Resources.resx`（zh-hans）和 `Resources.Designer.cs`
    - 可选：`Resources.en-US.resx` 和 `Resources.ja-JP.resx`，文件名必须保持现有的精确大小写
- `SecRandom/SecRandom.csproj` 只需要注册 `Resources.resx` 和 `Resources.Designer.cs`
  （照现有条目追加，不要把所有语言文件都注册进去）。

具体结构参考已有结构。

## 具体条例

### 设置界面本地化

按照 S_xxx(_D) 作为设置项本地化键名。
S_xxx 代表设置项名称；S_xxx_D 代表设置项描述（可以没有）。

中文 i18n 文案不得使用中文句号 `。`。
设置页面说明资源（`*_D`，包含 `S_*_D` 和 `C_*_D`）不得使用中文句号或英文句点，文件名、域名、进程名、版本号等技术标识中的英文点号保留。

也可使用 S_xxx_R 作为真正的本地化键，S_xxx 用作搜索时的描述。

（S 代表 Settings，D 代表 Description，R 代表 Real，）

---

ComboBoxItem 条目使用 O_xxx_yyy 作为本地化键名。
xxx 为选项所属设置名称，yyy 为选项键名。

消息文本使用 M_xxx 作为本地化键名，用于 ToastMessage、Logger 等。

其他内容（如版本公告等），按照 C_xxx 作为键名。

(O 代表 Options，M 代表 Messages，C 代表 Controls)
