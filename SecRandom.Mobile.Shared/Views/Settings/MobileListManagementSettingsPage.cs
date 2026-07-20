using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Mobile.Controls;
using SecRandom.Mobile.Services;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

public sealed partial class MobileListManagementSettingsPage : MobileSettingsPageBase
{
    private readonly IProfileCatalogManager _catalogManager;
    private readonly IProfileService _profileService;
    private readonly MobileMediaLibraryService _mediaLibrary;
    private readonly MobileDrawMediaService _drawMedia;
    private int _segment;
    private string? _studentListName;
    private string? _prizeListName;
    private StudentList? _studentList;
    private PrizeList? _prizeList;

    public MobileListManagementSettingsPage(
        IProfileCatalogManager catalogManager,
        IProfileService profileService,
        MobileMediaLibraryService mediaLibrary,
        MobileDrawMediaService drawMedia)
    {
        _catalogManager = catalogManager;
        _profileService = profileService;
        _mediaLibrary = mediaLibrary;
        _drawMedia = drawMedia;
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        if (_segment == 1 && !IsLotteryEnabled)
            _segment = 0;

        var segmented = MobileUi.CreateTabSplit(
            _segment,
            [
                (LR.S_StudentList, true),
                (LR.S_PrizePool, IsLotteryEnabled)
            ],
            segment =>
            {
                if (segment == _segment)
                    return;

                _segment = segment;
                Render();
            });

        var items = new List<Control> { segmented };
        items.AddRange(_segment == 0 ? BuildStudentSurface() : BuildPrizeSurface());
        RenderPage(items);
    }

    private IEnumerable<Control> BuildStudentSurface()
    {
        var names = _catalogManager.GetStudentListNames();
        _studentListName = ResolveProfileName(
            names,
            _studentListName,
            _profileService.StudentListConfig?.Name);
        _studentList = _studentListName is null ? null : _catalogManager.LoadStudentList(_studentListName);

        var selector = CreateProfileSelector(names, _studentListName, name =>
        {
            _studentListName = name;
            _profileService.LoadStudentProfile(name);
            _catalogManager.SetDefaultStudentList(name);
            Render();
        });
        var nameBox = new TextBox { PlaceholderText = LR.W_StudentName, MinHeight = 44 };
        var idBox = new TextBox { PlaceholderText = LR.W_StudentId, MinHeight = 44 };

        yield return CreateLabeledControl(LR.S_Profile, selector);
        yield return CreateAddForm(LR.C_AddStudent, FluentIcons.PersonAddFilled, nameBox, idBox,
            () => AddStudent(nameBox, idBox));
        yield return CreateTableHint();
        yield return _studentList is { Students.Count: > 0 }
            ? CreateStudentGrid(_studentList.Students.OrderForList().ToArray())
            : new MobileEmptyState(FluentIcons.PeopleFilled, LR.M_EmptyStudentList, LR.M_EmptyStudentListHint);
    }

    private IEnumerable<Control> BuildPrizeSurface()
    {
        var names = _catalogManager.GetPrizeListNames();
        _prizeListName = ResolveProfileName(
            names,
            _prizeListName,
            _profileService.PrizeListConfig?.Name);
        _prizeList = _prizeListName is null ? null : _catalogManager.LoadPrizeList(_prizeListName);

        var selector = CreateProfileSelector(names, _prizeListName, name =>
        {
            _prizeListName = name;
            _profileService.LoadPrizeProfile(name);
            _catalogManager.SetDefaultPrizePool(name);
            Render();
        });
        var nameBox = new TextBox { PlaceholderText = LR.W_PrizeName, MinHeight = 44 };
        var idBox = new TextBox { PlaceholderText = LR.W_PrizeId, MinHeight = 44 };

        yield return CreateLabeledControl(LR.S_Profile, selector);
        yield return CreateAddForm(LR.C_AddPrize, FluentIcons.GiftFilled, nameBox, idBox,
            () => AddPrize(nameBox, idBox));
        yield return CreateTableHint();
        yield return _prizeList is { Prizes.Count: > 0 }
            ? CreatePrizeGrid(_prizeList.Prizes.OrderForList().ToArray())
            : new MobileEmptyState(FluentIcons.GiftFilled, LR.M_EmptyPrizePool, LR.M_EmptyPrizePoolHint);
    }

    private DataGrid CreateStudentGrid(IReadOnlyList<Student> students)
    {
        var grid = CreateGrid(students.Count);
        grid.ItemsSource = students;
        grid.Columns.Add(CheckColumn(LR.H_Enabled, nameof(Student.Exists), 86));
        grid.Columns.Add(TextColumn(LR.H_Id, nameof(Student.Id), 110));
        grid.Columns.Add(TextColumn(LR.H_Name, nameof(Student.Name), 170));
        grid.Columns.Add(TextColumn(LR.H_Gender, nameof(Student.Gender), 100));
        grid.Columns.Add(TextColumn(LR.H_Group, nameof(Student.Group), 130));
        grid.Columns.Add(TextColumn(LR.H_Tags, nameof(Student.Tags), 160));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = LR.H_AttachedSettings,
            Width = new DataGridLength(96),
            CellTemplate = new FuncDataTemplate<Student>((student, _) => CreateAttachmentButton(student!))
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = LR.H_Operations,
            Width = new DataGridLength(86),
            CellTemplate = new FuncDataTemplate<Student>((student, _) => CreateStudentActions(student!))
        });
        return grid;
    }

    private DataGrid CreatePrizeGrid(IReadOnlyList<Prize> prizes)
    {
        var grid = CreateGrid(prizes.Count);
        grid.ItemsSource = prizes;
        grid.Columns.Add(CheckColumn(LR.H_Enabled, nameof(Prize.Exists), 86));
        grid.Columns.Add(TextColumn(LR.H_Id, nameof(Prize.Id), 110));
        grid.Columns.Add(TextColumn(LR.H_Name, nameof(Prize.Name), 170));
        grid.Columns.Add(TextColumn(LR.H_Weight, nameof(Prize.Weight), 100));
        grid.Columns.Add(TextColumn(LR.H_Inventory, nameof(Prize.Count), 100));
        grid.Columns.Add(TextColumn(LR.H_Tags, nameof(Prize.Tags), 160));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = LR.H_AttachedSettings,
            Width = new DataGridLength(96),
            CellTemplate = new FuncDataTemplate<Prize>((prize, _) => CreateAttachmentButton(prize!))
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = LR.H_Operations,
            Width = new DataGridLength(86),
            CellTemplate = new FuncDataTemplate<Prize>((prize, _) => CreatePrizeActions(prize!))
        });
        return grid;
    }

    private Control CreateStudentActions(Student student)
    {
        var more = CreateCompactIconButton(FluentIcons.MoreHorizontalFilled, LR.C_More,
            () => ShowStudentEditor(student.RecordId));
        var remove = CreateCompactIconButton(FluentIcons.DeleteFilled, LR.C_Remove,
            () => RemoveStudent(student.RecordId));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { more, remove }
        };
    }

    private Control CreatePrizeActions(Prize prize)
    {
        var more = CreateCompactIconButton(FluentIcons.MoreHorizontalFilled, LR.C_More,
            () => ShowPrizeEditor(prize.RecordId));
        var remove = CreateCompactIconButton(FluentIcons.DeleteFilled, LR.C_Remove,
            () => RemovePrize(prize.RecordId));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { more, remove }
        };
    }

    private Control CreateAttachmentButton(IAttachableSettingsObject record)
    {
        var button = CreateCompactIconButton(FluentIcons.SettingsCogMultipleFilled, LR.H_AttachedSettings,
            () => ShowAttachedSettings(record));
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { button }
        };
    }

    private async void ShowStudentEditor(Guid recordId)
    {
        var student = _studentList?.Students.FirstOrDefault(item => item.RecordId == recordId);
        if (student is null)
            return;

        var enabled = CreateToggle(student.Exists);
        var id = CreateEditor(LR.H_Id, student.Id);
        var name = CreateEditor(LR.H_Name, student.Name);
        var gender = CreateEditor(LR.H_Gender, student.Gender);
        var group = CreateEditor(LR.H_Group, student.Group);
        var tags = CreateEditor(LR.H_Tags, student.Tags);
        var form = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateLabeledControl(LR.H_Enabled, enabled),
                id.Container,
                name.Container,
                gender.Container,
                group.Container,
                tags.Container
            }
        };
        var result = await new FAContentDialog
        {
            Title = LR.M_MoreStudentTitle,
            Content = form,
            PrimaryButtonText = LR.C_Save,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return;

        if (string.IsNullOrWhiteSpace(id.Editor.Text) && string.IsNullOrWhiteSpace(name.Editor.Text))
        {
            await ShowMessageAsync(LR.M_RecordRequiresName);
            return;
        }
        student.Exists = enabled.IsChecked == true;
        student.Id = id.Editor.Text?.Trim() ?? string.Empty;
        student.Name = name.Editor.Text?.Trim() ?? string.Empty;
        student.Gender = gender.Editor.Text?.Trim() ?? string.Empty;
        student.Group = group.Editor.Text?.Trim() ?? string.Empty;
        student.Tags = tags.Editor.Text?.Trim() ?? string.Empty;
        SaveStudentList();
    }

    private async void ShowPrizeEditor(Guid recordId)
    {
        var prize = _prizeList?.Prizes.FirstOrDefault(item => item.RecordId == recordId);
        if (prize is null)
            return;

        var enabled = CreateToggle(prize.Exists);
        var id = CreateEditor(LR.H_Id, prize.Id);
        var name = CreateEditor(LR.H_Name, prize.Name);
        var weight = CreateEditor(LR.H_Weight, prize.Weight.ToString(System.Globalization.CultureInfo.CurrentCulture));
        var count = CreateEditor(LR.H_Inventory, prize.Count.ToString(System.Globalization.CultureInfo.CurrentCulture));
        var tags = CreateEditor(LR.H_Tags, prize.Tags);
        var form = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateLabeledControl(LR.H_Enabled, enabled),
                id.Container,
                name.Container,
                weight.Container,
                count.Container,
                tags.Container
            }
        };
        var result = await new FAContentDialog
        {
            Title = LR.M_MorePrizeTitle,
            Content = form,
            PrimaryButtonText = LR.C_Save,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return;

        if (string.IsNullOrWhiteSpace(id.Editor.Text) && string.IsNullOrWhiteSpace(name.Editor.Text))
        {
            await ShowMessageAsync(LR.M_RecordRequiresName);
            return;
        }
        if (!double.TryParse(weight.Editor.Text, out var parsedWeight) || parsedWeight < 0 ||
            !int.TryParse(count.Editor.Text, out var parsedCount) || parsedCount < 0)
        {
            await ShowMessageAsync(LR.M_InvalidPrizeValues);
            return;
        }
        prize.Exists = enabled.IsChecked == true;
        prize.Id = id.Editor.Text?.Trim() ?? string.Empty;
        prize.Name = name.Editor.Text?.Trim() ?? string.Empty;
        prize.Weight = parsedWeight;
        prize.Count = parsedCount;
        prize.Tags = tags.Editor.Text?.Trim() ?? string.Empty;
        SavePrizeList();
    }

    private void AddStudent(TextBox name, TextBox id)
    {
        if (_studentList is null || string.IsNullOrWhiteSpace(name.Text) && string.IsNullOrWhiteSpace(id.Text))
            return;

        var student = new Student
        {
            Name = name.Text?.Trim() ?? string.Empty,
            Id = id.Text?.Trim() ?? string.Empty
        };
        ProfileRecordIdentity.EnsureRecordId(student);
        _studentList.Students.Add(student);
        SaveStudentList();
    }

    private void AddPrize(TextBox name, TextBox id)
    {
        if (_prizeList is null || string.IsNullOrWhiteSpace(name.Text) && string.IsNullOrWhiteSpace(id.Text))
            return;

        var prize = new Prize
        {
            Name = name.Text?.Trim() ?? string.Empty,
            Id = id.Text?.Trim() ?? string.Empty
        };
        ProfileRecordIdentity.EnsureRecordId(prize);
        _prizeList.Prizes.Add(prize);
        SavePrizeList();
    }

    private async void RemoveStudent(Guid recordId)
    {
        var student = _studentList?.Students.FirstOrDefault(item => item.RecordId == recordId);
        if (student is null || !await ConfirmRemoveAsync(MobileUi.Format(student.Id, student.Name)))
            return;
        _studentList!.Students.Remove(student);
        SaveStudentList();
    }

    private async void RemovePrize(Guid recordId)
    {
        var prize = _prizeList?.Prizes.FirstOrDefault(item => item.RecordId == recordId);
        if (prize is null || !await ConfirmRemoveAsync(MobileUi.Format(prize.Id, prize.Name)))
            return;
        _prizeList!.Prizes.Remove(prize);
        SavePrizeList();
    }

    private async void ShowAttachedSettings(IAttachableSettingsObject record)
    {
        if (!TryResolveAttachmentRecord(record, out var current))
            return;

        var image = CopyImageSettings(current.GetAttachedObject<DrawImageAttachedSettings>(Guid.Parse(GlobalConstants.DrawImageAttachedSettings)));
        var music = CopyMusicSettings(current.GetAttachedObject<DrawMusicAttachedSettings>(Guid.Parse(GlobalConstants.DrawMusicAttachedSettings)));
        var voice = CopyVoiceSettings(current.GetAttachedObject<SpecificAnnouncementAttachedSettings>(Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings)));
        var importedImages = new List<string>();

        var imageEnabled = CreateToggle(image.IsAttachSettingsEnabled);
        var imagePreview = CreateImagePreview(image.ImagePath);
        var imagePath = new TextBlock { Text = string.IsNullOrWhiteSpace(image.ImagePath) ? LR.M_NoImageSelected : Path.GetFileName(image.ImagePath), TextWrapping = TextWrapping.Wrap };
        MobileTheme.BindBrush(imagePath, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);
        var imagePanel = new StackPanel { Spacing = 8, Children = { imageEnabled, imagePreview, imagePath } };
        var selectImage = MobileUi.CreateSecondaryButton(LR.C_SelectImage, () => _ = SelectImageAsync(image, imagePreview, imagePath, importedImages));
        var removeImage = MobileUi.CreateSecondaryButton(LR.C_RemoveImage, () =>
        {
            image.ImagePath = string.Empty;
            imagePreview.Source = null;
            imagePreview.IsVisible = false;
            imagePath.Text = LR.M_NoImageSelected;
        });
        imagePanel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Children = { selectImage, removeImage }
        });
        Grid.SetColumn(removeImage, 1);

        var musicEnabled = CreateToggle(music.IsAttachSettingsEnabled);
        var selections = _mediaLibrary.GetSelections();
        var animationMusic = CreateMusicSelector(selections, music.AnimationMusic);
        var resultMusic = CreateMusicSelector(selections, music.ResultMusic);
        var importMusic = MobileUi.CreateSecondaryButton(LR.C_ImportMusic, () => _ = ImportMusicAsync(animationMusic, resultMusic));
        var previewMusic = MobileUi.CreateSecondaryButton(LR.C_Preview, () => _ = PreviewMusicAsync(animationMusic));
        var stopMusic = MobileUi.CreateSecondaryButton(LR.C_Stop, () => _ = _drawMedia.StopAsync());
        var musicPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                musicEnabled,
                CreateLabeledControl(LR.S_AnimationMusic, animationMusic),
                CreateLabeledControl(LR.S_ResultMusic, resultMusic),
                importMusic
            }
        };
        if (_drawMedia.IsSupported)
            musicPanel.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 8,
                Children = { previewMusic, stopMusic }
            });
        if (_drawMedia.IsSupported)
            Grid.SetColumn(stopMusic, 1);

        var voiceEnabled = CreateToggle(voice.IsAttachSettingsEnabled);
        var alias = CreateEditor(LR.S_VoiceAlias, voice.TtsAlias);
        var prefix = CreateEditor(LR.S_VoicePrefix, voice.Prefix);
        var suffix = CreateEditor(LR.S_VoiceSuffix, voice.Suffix);
        var voicePanel = new StackPanel
        {
            Spacing = 8,
            Children = { voiceEnabled, alias.Container, prefix.Container, suffix.Container }
        };

        var formItems = new List<Control>
        {
            CreateAttachmentSection(LR.S_AttachedImage, FluentIcons.ImageFilled, imagePanel)
        };
        if (_drawMedia.IsSupported)
        {
            formItems.Add(CreateAttachmentSection(LR.S_AttachedMusic, FluentIcons.Speaker2Filled, musicPanel));
            formItems.Add(CreateAttachmentSection(LR.S_AttachedVoice, FluentIcons.IotFilled, voicePanel));
        }
        var form = new StackPanel { Spacing = 14, Children = { } };
        foreach (var item in formItems)
            form.Children.Add(item);
        var result = await new FAContentDialog
        {
            Title = LR.H_AttachedSettings,
            Content = new ScrollViewer { MaxHeight = 620, Content = form },
            PrimaryButtonText = LR.C_Save,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary || !TryResolveAttachmentRecord(record, out current))
        {
            foreach (var importedPath in importedImages)
                _mediaLibrary.DeleteImage(importedPath);
            return;
        }

        image.IsAttachSettingsEnabled = imageEnabled.IsChecked == true;
        var imageSettingsId = Guid.Parse(GlobalConstants.DrawImageAttachedSettings);
        var musicSettingsId = Guid.Parse(GlobalConstants.DrawMusicAttachedSettings);
        var voiceSettingsId = Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings);
        var priorImagePath = current.GetAttachedObject<DrawImageAttachedSettings>(imageSettingsId)?.ImagePath;
        var priorImageSettings = current.AttachedObjects.TryGetValue(imageSettingsId, out var imageAttachment)
            ? imageAttachment
            : null;
        var priorMusicSettings = current.AttachedObjects.TryGetValue(musicSettingsId, out var musicAttachment)
            ? musicAttachment
            : null;
        var priorVoiceSettings = current.AttachedObjects.TryGetValue(voiceSettingsId, out var voiceAttachment)
            ? voiceAttachment
            : null;
        var hadImageSettings = current.AttachedObjects.ContainsKey(imageSettingsId);
        var hadMusicSettings = current.AttachedObjects.ContainsKey(musicSettingsId);
        var hadVoiceSettings = current.AttachedObjects.ContainsKey(voiceSettingsId);
        current.WriteAttachedObject(imageSettingsId, image);
        if (_drawMedia.IsSupported)
        {
            music.IsAttachSettingsEnabled = musicEnabled.IsChecked == true;
            music.AnimationMusic = SelectedMusicId(animationMusic, MobileMediaLibraryService.NoMusicTrackId);
            music.ResultMusic = SelectedMusicId(resultMusic, MobileMediaLibraryService.NoMusicTrackId);
            voice.IsAttachSettingsEnabled = voiceEnabled.IsChecked == true;
            voice.TtsAlias = alias.Editor.Text?.Trim() ?? string.Empty;
            voice.Prefix = prefix.Editor.Text?.Trim() ?? string.Empty;
            voice.Suffix = suffix.Editor.Text?.Trim() ?? string.Empty;
            current.WriteAttachedObject(musicSettingsId, music);
            current.WriteAttachedObject(voiceSettingsId, voice);
        }
        if (!SaveCurrentList())
        {
            RestoreAttachment(current, imageSettingsId, hadImageSettings, priorImageSettings);
            RestoreAttachment(current, musicSettingsId, hadMusicSettings, priorMusicSettings);
            RestoreAttachment(current, voiceSettingsId, hadVoiceSettings, priorVoiceSettings);
            foreach (var importedPath in importedImages)
                _mediaLibrary.DeleteImage(importedPath);
            await ShowMessageAsync(LR.M_SaveFailed);
            return;
        }

        foreach (var importedPath in importedImages.Where(path => !string.Equals(path, image.ImagePath, StringComparison.Ordinal)))
            _mediaLibrary.DeleteImage(importedPath);
        if (!string.IsNullOrWhiteSpace(priorImagePath) && !string.Equals(priorImagePath, image.ImagePath, StringComparison.Ordinal))
            _mediaLibrary.DeleteImageIfUnreferenced(priorImagePath);
    }

    private async Task SelectImageAsync(
        DrawImageAttachedSettings image,
        Image preview,
        TextBlock pathText,
        ICollection<string> importedImages)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LR.S_AttachedImage)
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]
                }
            ]
        });
        var source = files.FirstOrDefault();
        if (source is null)
            return;

        string? imported = null;
        try
        {
            imported = await _mediaLibrary.ImportImageAsync(source);
            if (imported is null)
                throw new InvalidDataException();
            using var validation = new Bitmap(imported);
            image.ImagePath = imported;
            importedImages.Add(imported);
            preview.Source = new Bitmap(imported);
            preview.IsVisible = true;
            pathText.Text = Path.GetFileName(imported);
        }
        catch
        {
            if (imported is not null)
                _mediaLibrary.DeleteImage(imported);
            await ShowMessageAsync(LR.M_MediaImportFailed);
        }
    }

    private async Task ImportMusicAsync(ComboBox animation, ComboBox result)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LR.S_AttachedMusic) { Patterns = ["*.mp3", "*.wav", "*.flac"] }
            ]
        });
        var source = files.FirstOrDefault();
        if (source is null)
            return;

        try
        {
            var imported = await _mediaLibrary.ImportMusicAsync(source);
            if (imported is null)
                throw new InvalidDataException();
            var selections = _mediaLibrary.GetSelections();
            animation.ItemsSource = selections;
            result.ItemsSource = selections;
            animation.SelectedItem = selections.FirstOrDefault(selection => selection.Id == imported);
        }
        catch
        {
            await ShowMessageAsync(LR.M_MediaImportFailed);
        }
    }

    private async Task PreviewMusicAsync(ComboBox selector)
    {
        if (selector.SelectedItem is MobileMediaSelection selection)
            await _drawMedia.PreviewAsync(selection.Id);
    }

    private bool TryResolveAttachmentRecord(IAttachableSettingsObject record, out IAttachableSettingsObject current)
    {
        current = record;
        switch (record)
        {
            case Student student:
                current = _studentList?.Students.FirstOrDefault(item => item.RecordId == student.RecordId)!;
                return current is not null;
            case Prize prize:
                current = _prizeList?.Prizes.FirstOrDefault(item => item.RecordId == prize.RecordId)!;
                return current is not null;
            default:
                return false;
        }
    }

    private static MobileCard CreateAttachmentSection(string title, string glyph, Control content) => new()
    {
        Content = new StackPanel { Spacing = 8, Children = { new MobileSectionHeader(title, glyph), content } }
    };

    private static Image CreateImagePreview(string path)
    {
        var preview = new Image
        {
            Height = 112,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false
        };
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                preview.Source = new Bitmap(path);
                preview.IsVisible = true;
            }
            catch { }
        }
        return preview;
    }

    private static ComboBox CreateMusicSelector(IReadOnlyList<MobileMediaSelection> selections, string selected) => new()
    {
        ItemsSource = selections,
        SelectedItem = selections.FirstOrDefault(selection => selection.Id == selected) ?? selections.FirstOrDefault(),
        MinHeight = 44,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemTemplate = new FuncDataTemplate<MobileMediaSelection>((selection, _) => new TextBlock
        {
            Text = selection?.DisplayName ?? string.Empty,
            TextTrimming = TextTrimming.CharacterEllipsis
        })
    };

    private static string SelectedMusicId(ComboBox selector, string fallback) =>
        selector.SelectedItem is MobileMediaSelection selection ? selection.Id : fallback;

    private async Task ShowMessageAsync(string message)
    {
        await new FAContentDialog
        {
            Title = LR.H_AttachedSettings,
            Content = message,
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private async Task<bool> ConfirmRemoveAsync(string displayName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.C_Remove,
            Content = string.IsNullOrWhiteSpace(displayName) ? LR.C_Remove : displayName,
            PrimaryButtonText = LR.C_Remove,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));
        return result == FAContentDialogResult.Primary;
    }

    private static DrawImageAttachedSettings CopyImageSettings(DrawImageAttachedSettings? source) => new()
    {
        IsAttachSettingsEnabled = source?.IsAttachSettingsEnabled ?? false,
        ImagePath = source?.ImagePath ?? string.Empty
    };

    private static DrawMusicAttachedSettings CopyMusicSettings(DrawMusicAttachedSettings? source) => new()
    {
        IsAttachSettingsEnabled = source?.IsAttachSettingsEnabled ?? false,
        AnimationMusic = source?.AnimationMusic ?? MobileMediaLibraryService.NoMusicTrackId,
        ResultMusic = source?.ResultMusic ?? MobileMediaLibraryService.NoMusicTrackId
    };

    private static SpecificAnnouncementAttachedSettings CopyVoiceSettings(SpecificAnnouncementAttachedSettings? source) => new()
    {
        IsAttachSettingsEnabled = source?.IsAttachSettingsEnabled ?? false,
        TtsAlias = source?.TtsAlias ?? string.Empty,
        Prefix = source?.Prefix ?? string.Empty,
        Suffix = source?.Suffix ?? string.Empty
    };

    private bool SaveCurrentList()
    {
        try
        {
            var saved = _segment == 0
                ? _studentList is not null && _catalogManager.SaveStudentList(_studentList)
                : _prizeList is not null && _catalogManager.SavePrizeList(_prizeList);
            if (saved)
                Render();
            return saved;
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreAttachment(
        IAttachableSettingsObject record,
        Guid settingsId,
        bool hadSettings,
        object? settings)
    {
        if (hadSettings)
            record.AttachedObjects[settingsId] = settings;
        else
            record.AttachedObjects.Remove(settingsId);
    }

    private void SaveStudentList(bool render = true)
    {
        if (_studentList is not null && _catalogManager.SaveStudentList(_studentList) && render)
            Render();
    }

    private void SavePrizeList(bool render = true)
    {
        if (_prizeList is not null && _catalogManager.SavePrizeList(_prizeList) && render)
            Render();
    }

    private static DataGrid CreateGrid(int rowCount) => new()
    {
        AutoGenerateColumns = false,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        IsReadOnly = true,
        Height = 58 + rowCount * 48,
        RowHeight = 48,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static DataGridTextColumn TextColumn(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width)
    };

    private static DataGridCheckBoxColumn CheckColumn(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width)
    };

    private static ComboBox CreateProfileSelector(
        IReadOnlyList<string> names,
        string? selected,
        Action<string> changed)
    {
        var combo = new ComboBox
        {
            ItemsSource = names,
            SelectedItem = selected,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value && !string.Equals(value, selected, StringComparison.Ordinal))
                changed(value);
        };
        return combo;
    }

    private static string? ResolveProfileName(
        IReadOnlyList<string> names,
        string? selected,
        string? current)
    {
        if (selected is not null && names.Contains(selected, StringComparer.Ordinal))
            return selected;
        if (current is not null && names.Contains(current, StringComparer.Ordinal))
            return current;
        return names.FirstOrDefault();
    }

    private static MobileCard CreateAddForm(
        string title,
        string glyph,
        TextBox nameBox,
        TextBox idBox,
        Action add)
    {
        var addButton = MobileUi.CreatePrimaryButton(title, true, add);
        return new MobileCard
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { new MobileSectionHeader(title, glyph), nameBox, idBox, addButton }
            }
        };
    }

    private static TextBlock CreateTableHint()
    {
        var hint = new TextBlock { Text = LR.M_ListTableHint, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        MobileTheme.BindBrush(hint, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);
        return hint;
    }

    private static Control CreateLabeledControl(string label, Control control)
    {
        var text = new TextBlock { Text = label, FontSize = 12 };
        MobileTheme.BindBrush(text, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);
        return new StackPanel { Spacing = 4, Children = { text, control } };
    }

    private static (Control Container, TextBox Editor) CreateEditor(string label, string value)
    {
        var editor = new TextBox { Text = value, MinHeight = 44 };
        return (CreateLabeledControl(label, editor), editor);
    }

    private static ToggleSwitch CreateToggle(bool value) => new()
    {
        IsChecked = value,
        OnContent = string.Empty,
        OffContent = string.Empty
    };

    private static Button CreateCompactIconButton(string glyph, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = MobileUi.CreateIcon(glyph, 18, HorizontalAlignment.Center),
            MinWidth = 44,
            MinHeight = 40,
            Padding = new Thickness(8),
            [ToolTip.TipProperty] = tooltip
        };
        button.Click += (_, _) => action();
        return button;
    }
}
