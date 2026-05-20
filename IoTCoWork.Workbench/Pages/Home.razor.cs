using AntDesign;
using AntDesign.X;
using AntDesign.X.Components;
using IoTCoWork.Workbench.Models;
using IoTCoWork.Workbench.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using QRCoder;
using System.Reflection;

namespace IoTCoWork.Workbench.Pages;

public partial class Home
{
    private const string AppName = "IoTCoWork";
    private static readonly string AppVersion = FormatAppVersion(
        typeof(Home).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Home).Assembly.GetName().Version?.ToString(3)
        ?? "0.1.0");

    private static string FormatAppVersion(string version)
    {
        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        version = version.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            return "v0.1.0";
        }

        return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v{version}";
    }

    private static readonly XThemeTokens LightTokens = new()
    {
        PrimaryColor = "#007f73",
        BorderRadius = "8px",
        ColorBgChat = "#f5f7fb",
        ColorBgBubbleUser = "#0f766e",
        ColorBgBubbleAi = "#ffffff",
        ColorTextBubbleUser = "#ffffff",
        ColorTextBubbleAi = "#172033",
        ColorBorderBubble = "#dbe4ef",
        FontFamily = "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif",
    };

    private static readonly XThemeTokens DarkTokens = new()
    {
        PrimaryColor = "#2dd4bf",
        BorderRadius = "8px",
        ColorBgChat = "#101827",
        ColorBgBubbleUser = "#0f766e",
        ColorBgBubbleAi = "#172033",
        ColorTextBubbleUser = "#ecfeff",
        ColorTextBubbleAi = "#e5edf7",
        ColorBorderBubble = "#2b3a4e",
        FontFamily = "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif",
    };

    private readonly IReadOnlyList<AspectRatioPreset> _aspectRatioPresets =
    [
        new("1:1"),
        new("3:4"),
        new("2:3"),
        new("9:16"),
        new("3:2"),
        new("4:3"),
        new("16:9", "电影"),
        new("21:9", "电影"),
    ];
    private readonly IReadOnlyList<ResolutionPreset> _resolutionPresets =
    [
        new("1k", "1K", "快速预览，最长边约 1,024px"),
        new("2k", "2K", "标准输出，最长边约 1,792px"),
        new("4k", "2MP", "高清画幅，最长边约 2,048px"),
        new("8mp", "8MP Max", "官方最大画幅，最高约 8.29MP / 3840px"),
    ];
    private readonly IReadOnlyList<string> _modelOptions =
    [
        "gpt-image-2",
        "gpt-image-2-2026-04-21",
    ];
    private readonly IReadOnlyList<string> _moderationOptions = ["auto", "low"];
    private readonly IReadOnlyList<string> _fidelityOptions = ["默认", "high", "low"];
    private readonly IReadOnlyList<PromptPolishOption> _promptPolishOptions =
    [
        new("direct", "直接生成", "不再每次询问，按原提示词生成。"),
        new("ask", "每次询问", "提交作图需求后询问是否先润色。"),
        new("auto", "自动润色", "先润色提示词，再生成图片。"),
    ];
    private readonly IReadOnlyList<string> _promptIdeas =
    [
        "一张极简产品海报，玻璃茶杯悬浮在白色背景中",
        "赛博城市夜景，中文霓虹标牌，电影级构图",
        "毛玻璃质感的 AI 作图应用图标，适合桌面端",
    ];
    private const string PromptConfirmRole = "prompt-confirm";
    private StudioSnapshot _snapshot = CreateInitialSnapshot();
    private string _senderText = string.Empty;
    private int _count = 1;
    private bool _loading;
    private string _loadingLabel = "正在请求图像接口";
    private bool _leftSidebarCollapsed;
    private bool _settingsOpen;
    private bool _accountMenuOpen;
    private bool _capabilityCenterOpen;
    private bool _exitConfirmOpen;
    private bool _accountBusy;
    private bool _accountRegisterOpen;
    private bool _authProxyOpen;
    private string? _error;
    private string? _downloadNotice;
    private string? _lastDownloadFilePath;
    private bool _revealDownloadBusy;
    private string? _lastRawJson;
    private string _accountEmail = string.Empty;
    private string _accountPassword = string.Empty;
    private string? _accountMessage;
    private bool _accountMessageIsError;
    private decimal _rechargeAmount = 10;
    private SaaSRechargeOrder? _activePaymentOrder;
    private SaaSRechargeOrder? _latestPaymentStatus;
    private bool _paymentOverlayOpen;
    private bool _paymentPolling;
    private bool _paymentCancelling;
    private string? _paymentQrImageSource;
    private DateTimeOffset? _paymentExpiresAt;
    private PeriodicTimer? _paymentTimer;
    private CancellationTokenSource? _paymentCts;
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _serverStatusTimer;
    private CancellationTokenSource? _serverStatusCts;
    private DotNetObjectReference<Home>? _selfReference;
    private string? _systemThemeWatchId;
    private string? _lastDocumentTheme;
    private bool _systemPrefersDark;
    private bool _serverOnline;
    private bool _serverStatusChecked;
    private DateTimeOffset? _serverStatusCheckedAt;
    private bool _updateChecking;
    private bool _updateInstalling;
    private AppUpdateCheckResponse? _updateInfo;
    private string? _updateNotice;
    private bool _updateNoticeIsError;
    private PendingPrompt? _pendingPrompt;
    private bool _ratioMenuOpen;
    private bool _resolutionMenuOpen;
    private GeneratedImage? _previewImage;
    private string _previewAlt = string.Empty;
    private bool _previewZoomed;
    private bool _previewEditing;
    private bool _previewMaskAttachPending;
    private int _previewBrushSize = 36;
    private string _previewEditPrompt = string.Empty;
    private string? _previewEditError;
    private ElementReference _previewImageElement;
    private ElementReference _previewMaskCanvas;
    private readonly List<ImageReferenceFile> _referenceFiles = [];
    private readonly List<SenderImageAttachment> _senderImageAttachments = [];
    private IReadOnlyList<XAttachmentItem> _senderAttachments = [];

    private StudioSettings Settings => _snapshot.Settings;

    private StudioSession ActiveSession =>
        _snapshot.Sessions.FirstOrDefault(session => session.Id == _snapshot.ActiveSessionId)
        ?? _snapshot.Sessions.First();

    private bool AccountLoggedIn => !string.IsNullOrWhiteSpace(Settings.PlatformAccessToken);
    private bool AccountReady => AccountLoggedIn &&
        !string.IsNullOrWhiteSpace(Settings.CloudAccessToken);
    private bool RequiresAuthOverlay => !AccountReady;
    private string EffectiveTheme => Settings.ThemeMode == "dark" || (Settings.ThemeMode == "system" && _systemPrefersDark)
        ? "dark"
        : "light";
    private string ProviderTheme => EffectiveTheme;
    private XThemeTokens ThemeTokens => EffectiveTheme == "dark" ? DarkTokens : LightTokens;
    private string RootThemeClass => $"studio-provider theme-{EffectiveTheme}";
    private string AccountEmail => string.IsNullOrWhiteSpace(Settings.SaaSUser?.Email) ? "未登录" : Settings.SaaSUser.Email;
    private string AccountDisplayName => ResolveAccountDisplayName();
    private string AccountAvatarUrl => Settings.SaaSUser?.AvatarUrl?.Trim() ?? string.Empty;
    private string AccountInitials => ResolveAccountInitials(AccountDisplayName, Settings.SaaSUser?.Email);
    private string AccountConnectionLabel => AccountReady
        ? "已连接"
        : AccountLoggedIn
            ? "待完成云端连接"
            : "未登录";
    private string AccountServiceLabel => string.IsNullOrWhiteSpace(Settings.SaaSUser?.AccountService?.Status)
        ? "未配置"
        : Settings.SaaSUser.AccountService.Status;
    private string AccountServiceDetail => string.IsNullOrWhiteSpace(Settings.SaaSUser?.AccountService?.Detail)
        ? "暂无账户服务状态"
        : Settings.SaaSUser.AccountService.Detail;
    private string AccountMenuButtonTitle => AccountLoggedIn ? $"账户：{AccountDisplayName}" : "账户菜单";
    private string AccountMenuExpanded => _accountMenuOpen ? "true" : "false";
    private string AccountAvatarButtonClass => $"account-avatar-button{(_accountMenuOpen ? " active" : string.Empty)}";
    private string AccountPresenceClass => AccountReady
        ? "account-presence is-ready"
        : AccountLoggedIn
            ? "account-presence is-partial"
            : "account-presence";
    private string BalanceLabel => Settings.SaaSUser is null ? "--" : FormatMoney(Settings.SaaSUser.Balance);
    private string HeaderSubtitle => AppVersion;
    private string AccountMessageClass => _accountMessageIsError ? "settings-message error" : "settings-message";
    private string PromptPolishModeLabel =>
        _promptPolishOptions.FirstOrDefault(option => option.Value == Settings.PromptPolishMode)?.Label ?? "直接生成";
    private string ServerStatusClass => _serverOnline
        ? "server-status online"
        : _serverStatusChecked
            ? "server-status offline"
            : "server-status checking";
    private string ServerStatusText => _serverOnline
        ? "服务联机正常"
        : _serverStatusChecked
            ? "服务连接异常"
            : "正在检查服务";
    private string ServerStatusTitle => _serverStatusCheckedAt is null
        ? ServerStatusText
        : $"{ServerStatusText} · {_serverStatusCheckedAt.Value.ToLocalTime():HH:mm:ss}";
    private string UpdateStatusLabel => _updateInstalling
        ? "正在安装"
        : _updateChecking
            ? "正在检查"
            : _updateInfo?.UpdateAvailable == true
                ? "发现新版本"
                : _updateInfo is not null
                    ? "已是最新"
                    : "未检查";
    private string LatestUpdateVersionLabel => _updateInfo?.LatestVersionDisplay ?? "--";
    private string UpdateMessage => _updateNotice
        ?? _updateInfo?.Message
        ?? "可从 GitHub Release 检查并安装新版本。";
    private string UpdatePanelClass =>
        $"update-panel{(_updateInfo?.UpdateAvailable == true ? " has-update" : string.Empty)}{(_updateNoticeIsError ? " error" : string.Empty)}";
    private string BodyClass =>
        $"studio-body{(_leftSidebarCollapsed ? " left-collapsed" : string.Empty)}";
    private string SidebarClass => _leftSidebarCollapsed ? "studio-sidebar is-collapsed" : "studio-sidebar";
    private string CanvasClass => _loading ? "workspace-canvas is-loading" : "workspace-canvas";
    private string ContextServerStatusClass => $"{ServerStatusClass} compact";
    private string WorkspaceStatus => _loading ? "正在生成，请保持窗口打开" : LatestImages.Count == 0 ? "已就绪，可以开始作图" : $"已生成 {LatestImages.Count} 张图片";
    private string AuthButtonText => _accountBusy ? "处理中..." : _accountRegisterOpen ? "创建并登录" : "登录";
    private string PaymentTitle => PaymentSucceeded
        ? "支付完成"
        : PaymentExpired
            ? "订单已过期"
            : "扫码完成支付";
    private string PaymentStatusLabel => PaymentSucceeded
        ? "已完成"
        : PaymentExpired
            ? "已过期"
            : _paymentPolling
                ? "确认中"
                : "待支付";
    private string PaymentCountdown => RemainingPaymentSeconds <= 0
        ? "00:00"
        : $"{RemainingPaymentSeconds / 60:00}:{RemainingPaymentSeconds % 60:00}";
    private string? PaymentQrImageSource => _paymentQrImageSource;
    private int RemainingPaymentSeconds => _paymentExpiresAt is null
        ? 0
        : Math.Max(0, (int)Math.Ceiling((_paymentExpiresAt.Value - DateTimeOffset.Now).TotalSeconds));
    private bool PaymentSucceeded => IsPaymentSuccess(_latestPaymentStatus?.Status);
    private bool PaymentExpired => RemainingPaymentSeconds <= 0 || IsPaymentExpired(_latestPaymentStatus?.Status);
    private bool HasPendingPaymentOrder => _activePaymentOrder is not null && !PaymentSucceeded && !PaymentExpired;
    private string ModeLabel => ActiveSession.Mode switch
    {
        "image" => "图生图",
        "edit" => "图片编辑",
        "variation" => "变化",
        _ => "文生图",
    };
    private string InputFileLabel => ActiveSession.Mode == "edit" ? "待编辑图片" : "参考图";
    private string InputFileAccept => "image/*";
    private AspectRatioPreset CurrentAspectRatio =>
        _aspectRatioPresets.FirstOrDefault(preset => preset.Ratio == EffectiveAspectRatio)
        ?? _aspectRatioPresets[0];
    private ResolutionPreset CurrentResolution =>
        AvailableResolutionPresets.FirstOrDefault(preset => preset.Tier == Settings.ResolutionTier)
        ?? _resolutionPresets.First(preset => preset.Tier == "2k");
    private string EffectiveAspectRatio => ResolveAspectRatio(Settings.AspectRatio, Settings.Size);
    private ImageModelCapabilities CurrentModelCapabilities => ImageModelCatalog.Get(Settings.Model, ActiveSession.Mode);
    private IReadOnlyList<ResolutionPreset> AvailableResolutionPresets =>
        IsGptImage2Model(CurrentModelCapabilities.Model)
            ? _resolutionPresets
            : _resolutionPresets.Where(preset => preset.Tier is "1k" or "2k").ToArray();
    private IReadOnlyList<string> CurrentQualityOptions => CurrentModelCapabilities.Qualities;
    private IReadOnlyList<string> CurrentFormatOptions => CurrentModelCapabilities.OutputFormats.Count == 0
        ? ["默认"]
        : CurrentModelCapabilities.OutputFormats;
    private IReadOnlyList<string> CurrentBackgroundOptions => CurrentModelCapabilities.Backgrounds.Count == 0
        ? ["默认"]
        : CurrentModelCapabilities.Backgrounds;
    private IReadOnlyList<string> CurrentStyleOptions => CurrentModelCapabilities.Styles.Count == 0
        ? ["默认"]
        : CurrentModelCapabilities.Styles;
    private string QuickSettingsSummary =>
        $"{Settings.Model} · 实际 {EffectiveImageSize} · {CurrentAspectRatio.Label} · {QualityOptionLabel(EffectiveQuality)} · {EffectiveFormatLabel}";
    private string EffectiveImageSize => BuildImageSize(EffectiveAspectRatio, Settings.ResolutionTier);
    private string EffectiveQuality => CurrentModelCapabilities.Qualities.Contains(Settings.Quality) ? Settings.Quality : CurrentModelCapabilities.Qualities[0];
    private string EffectiveFormatLabel => CurrentModelCapabilities.SupportsOutputFormat ? Settings.Format.ToUpperInvariant() : "默认格式";
    private string ActualRequestSummary =>
        $"实际参数：model={Settings.Model}，size={EffectiveImageSize}，quality={EffectiveQuality}" +
        (CurrentModelCapabilities.SupportsOutputFormat ? $"，output_format={Settings.Format}" : string.Empty) +
        (CurrentModelCapabilities.SupportsBackground ? $"，background={Settings.Background}" : string.Empty) +
        (CurrentModelCapabilities.SupportsStream ? $"，request={Settings.RequestMode}" : "，request=sync") +
        $"。{CurrentModelCapabilities.SizeNote}";
    private string CurrentRatioPreviewClass => RatioPreviewClass(CurrentAspectRatio);
    private string ImagePreviewTitle => string.IsNullOrWhiteSpace(_previewAlt) ? "生成图片" : _previewAlt;
    private string ImagePreviewDialogClass =>
        $"image-preview-dialog{(_previewZoomed ? " is-zoomed" : string.Empty)}{(_previewEditing ? " is-editing" : string.Empty)}";
    private string ImagePreviewStageClass => _previewEditing ? "image-preview-stage is-editing" : "image-preview-stage";
    private string ImagePreviewEditButtonClass => _previewEditing ? "active" : string.Empty;
    private bool PreviewEditSubmitDisabled => _loading || _previewImage is null || string.IsNullOrWhiteSpace(_previewEditPrompt);
    private bool CanRevealDownloadedFile => !string.IsNullOrWhiteSpace(_lastDownloadFilePath);

    private IReadOnlyList<GeneratedImage> LatestImages =>
        ActiveSession.Messages.LastOrDefault(message => message.Images.Count > 0)?.Images ?? [];

    private IReadOnlyList<XConversationItem> ConversationItems =>
        _snapshot.Sessions
            .OrderByDescending(session => session.UpdatedAt)
            .Select(session => new XConversationItem
            {
                Key = session.Id,
                Title = session.Title,
                Description = $"{ModeName(session.Mode)} · {session.UpdatedAt.ToLocalTime():MM-dd HH:mm}",
                Icon = session.Messages.Count > 0 ? "picture" : "message",
                Group = session.UpdatedAt.Date == DateTimeOffset.Now.Date ? "今天" : "更早",
                Count = session.Messages.Count == 0 ? null : session.Messages.Count,
                UpdatedAt = session.UpdatedAt,
            })
            .ToArray();

    protected override async Task OnInitializedAsync()
    {
        _snapshot = await Storage.LoadAsync();

        EnsureActiveMode();
        StartServerStatusPolling();
        await RestoreAccountAsync();
        _ = CheckForUpdatesAsync(silent: true);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _systemPrefersDark = await JsRuntime.InvokeAsync<bool>("imageStudio.prefersDarkTheme");
                _selfReference = DotNetObjectReference.Create(this);
                _systemThemeWatchId = await JsRuntime.InvokeAsync<string>("imageStudio.watchSystemTheme", _selfReference);
            }
            catch (JSException)
            {
            }
        }

        await SyncDocumentThemeAsync();

        if (firstRender)
        {
            StateHasChanged();
        }

        if (_previewMaskAttachPending)
        {
            _previewMaskAttachPending = false;
            await ResizePreviewMaskAsync();
        }
    }

    private static StudioSnapshot CreateInitialSnapshot()
    {
        var session = new StudioSession();
        return new StudioSnapshot
        {
            Settings = new StudioSettings(),
            Sessions = [session],
            ActiveSessionId = session.Id,
        };
    }

    private string TabClass(string mode) => ActiveSession.Mode == mode ? "active" : string.Empty;
    private string ThemeOptionClass(string mode) => Settings.ThemeMode == mode ? "active" : string.Empty;
    private static string ResolveAspectRatio(string? aspectRatio, string? size)
    {
        var normalized = StudioSettings.NormalizeAspectRatio(aspectRatio);
        if (normalized != "auto")
        {
            return normalized;
        }

        return size switch
        {
            "1024x1024" or "2048x2048" or "2880x2880" or "512x512" or "256x256" => "1:1",
            "896x1152" or "1024x1360" or "1536x2048" or "2448x3264" => "3:4",
            "832x1248" or "1024x1536" or "1360x2048" or "2336x3504" => "2:3",
            "768x1360" or "1024x1792" or "1152x2048" or "2160x3840" => "9:16",
            "1248x832" or "1536x1024" or "2048x1360" or "3504x2336" => "3:2",
            "1152x896" or "1360x1024" or "2048x1536" or "3264x2448" => "4:3",
            "1360x768" or "1792x1024" or "2048x1152" or "3840x2160" => "16:9",
            "1536x640" or "1792x768" or "2048x896" or "3840x1648" => "21:9",
            _ => "1:1",
        };
    }

    private static string BuildImageSize(string aspectRatio, string resolutionTier)
    {
        aspectRatio = StudioSettings.NormalizeAspectRatio(aspectRatio);
        resolutionTier = StudioSettings.NormalizeResolutionTier(resolutionTier);

        return (resolutionTier, aspectRatio) switch
        {
            ("1k", "1:1") => "1024x1024",
            ("1k", "3:4") => "896x1152",
            ("1k", "2:3") => "832x1248",
            ("1k", "9:16") => "768x1360",
            ("1k", "3:2") => "1248x832",
            ("1k", "4:3") => "1152x896",
            ("1k", "16:9") => "1360x768",
            ("1k", "21:9") => "1536x640",
            ("4k", "1:1") => "2048x2048",
            ("4k", "3:4") => "1536x2048",
            ("4k", "2:3") => "1360x2048",
            ("4k", "9:16") => "1152x2048",
            ("4k", "3:2") => "2048x1360",
            ("4k", "4:3") => "2048x1536",
            ("4k", "16:9") => "2048x1152",
            ("4k", "21:9") => "2048x896",
            ("8mp", "1:1") => "2880x2880",
            ("8mp", "3:4") => "2448x3264",
            ("8mp", "2:3") => "2336x3504",
            ("8mp", "9:16") => "2160x3840",
            ("8mp", "3:2") => "3504x2336",
            ("8mp", "4:3") => "3264x2448",
            ("8mp", "16:9") => "3840x2160",
            ("8mp", "21:9") => "3840x1648",
            (_, "3:4") => "1024x1360",
            (_, "2:3") => "1024x1536",
            (_, "9:16") => "1024x1792",
            (_, "3:2") => "1536x1024",
            (_, "4:3") => "1360x1024",
            (_, "16:9") => "1792x1024",
            (_, "21:9") => "1792x768",
            _ => "1024x1024",
        };
    }

    private string RatioPreviewClass(AspectRatioPreset preset) =>
        $"quick-ratio-preview ratio-{preset.Ratio.Replace(':', '-')}";

    private string RatioOptionClass(AspectRatioPreset preset) =>
        IsCurrentAspectRatio(preset) ? "active" : string.Empty;

    private string ResolutionOptionClass(ResolutionPreset preset) =>
        IsCurrentResolution(preset) ? "active" : string.Empty;

    private bool IsCurrentAspectRatio(AspectRatioPreset preset) =>
        string.Equals(CurrentAspectRatio.Ratio, preset.Ratio, StringComparison.Ordinal);

    private bool IsCurrentResolution(ResolutionPreset preset) =>
        string.Equals(CurrentResolution.Tier, preset.Tier, StringComparison.Ordinal);

    private static string QualityOptionLabel(string value) => value switch
    {
        "auto" => "自动",
        "high" => "高",
        "medium" => "中",
        "low" => "低",
        "hd" => "HD",
        "standard" => "标准",
        "默认" => "默认",
        _ => value,
    };
    private static bool IsGptImage2Model(string model) =>
        model.Equals("gpt-image-2", StringComparison.OrdinalIgnoreCase) ||
        model.Equals("gpt-image-2-2026-04-21", StringComparison.OrdinalIgnoreCase);
    private void CoerceImageSettingsForCurrentModel()
    {
        ImageModelCatalog.NormalizeSettings(Settings, ActiveSession.Mode);
        Settings.ResolutionTier = NormalizeResolutionTierForModel(Settings.ResolutionTier, CurrentModelCapabilities);
        Settings.AspectRatio = EffectiveAspectRatio;
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        ImageModelCatalog.NormalizeSettings(Settings, ActiveSession.Mode);
    }

    private static string NormalizeResolutionTierForModel(string? tier, ImageModelCapabilities capabilities)
    {
        var normalized = StudioSettings.NormalizeResolutionTier(tier);
        if (IsGptImage2Model(capabilities.Model))
        {
            return normalized;
        }

        return normalized is "4k" or "8mp" ? "2k" : normalized;
    }
    private static string ThemeModeLabel(string mode) => mode switch
    {
        "light" => "浅色",
        "dark" => "深色",
        _ => "系统",
    };
    private string PromptPolishOptionClass(string mode) =>
        Settings.PromptPolishMode == mode ? "active" : string.Empty;
    private string AuthTabClass(bool register) => _accountRegisterOpen == register ? "active" : string.Empty;
    private void SetLeftSidebarCollapsed(bool collapsed) => _leftSidebarCollapsed = collapsed;
    private static string MessageClass(StudioMessage message) => $"thread-message {message.Role}";
    private static string MessageRoleLabel(string role) => role switch
    {
        "user" => "我",
        "assistant" => "图像结果",
        PromptConfirmRole => "提示词",
        "system" => "系统",
        _ => "消息",
    };
    private static bool ShouldShowMessageText(StudioMessage message) =>
        !string.IsNullOrWhiteSpace(message.Content) &&
        (message.Images.Count == 0 || !string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase));

    private async Task GenerateAsync(string? promptOverride = null, bool addUserMessage = true)
    {
        if (_loading)
        {
            return;
        }

        if (!AccountReady)
        {
            _error = "请先登录账户。";
            _settingsOpen = false;
            SetAccountMessage("登录后会自动完成作图配置。", isError: false);
            return;
        }

        var prompt = (promptOverride ?? ActiveSession.Prompt).Trim();
        var requestMode = ResolveRequestMode(prompt, ActiveSession.Mode);
        ApplyResolvedMode(requestMode);

        if (prompt.Length == 0 && requestMode != "variation")
        {
            _error = "请先填写提示词。";
            return;
        }

        _error = null;
        ClearDownloadNotice();
        _loading = true;
        _loadingLabel = "正在请求图像接口";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var completed = false;

        if (addUserMessage && prompt.Length > 0)
        {
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "user",
                Content = prompt,
            });
            TouchActiveSession(prompt);
        }

        StateHasChanged();
        await SaveAsync();

        try
        {
            var result = await ImageClient.GenerateAsync(
                new StudioImageRequest(CreateRequestSettings(), prompt, ParseReferences(), ActiveSession.MaskReference, _referenceFiles, _count, requestMode),
                _cts.Token);

            _lastRawJson = result.RawJson;
            var durableImages = await PersistGeneratedImagesAsync(result.Images, _cts.Token);
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "assistant",
                Content = durableImages.Count == 0 ? "接口调用成功，但响应里没有解析到图片。" : "图片生成完成。",
                Images = durableImages.ToList(),
            });
            completed = true;
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = "本次生成已取消。",
            });
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = $"生成失败：{ex.Message}",
            });
        }
        finally
        {
            _loading = false;
            if (completed)
            {
                ClearSenderAttachments();
            }
            TouchActiveSession();
            await SaveAsync();
            StateHasChanged();
        }
    }

    private async Task SendFromSender(XSenderRequest request)
    {
        var text = (request.Text ?? string.Empty).Trim();
        if (_loading || (text.Length == 0 && !HasImageInputs()))
        {
            return;
        }

        await HandleUserTextAsync(text);
    }

    private async Task HandleUserTextAsync(string text)
    {
        if (!AccountReady)
        {
            _error = "请先登录账户。";
            _settingsOpen = false;
            SetAccountMessage("登录后会自动完成作图配置。", isError: false);
            return;
        }

        _error = null;
        ClearDownloadNotice();
        _pendingPrompt = null;
        RemovePendingPromptMessages();

        if (TryCreateLatestImageRevision(text, out var revision))
        {
            await GenerateImageRevisionAsync(revision.ImageUrl, revision.Prompt, BuildUserMessageContent(text, "edit"));
            return;
        }

        var requestMode = ResolveRequestMode(text, ActiveSession.Mode);
        ApplyResolvedMode(requestMode);
        _loading = true;
        _loadingLabel = "正在理解需求";
        _senderText = string.Empty;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        ActiveSession.Messages.Add(new StudioMessage
        {
            Role = "user",
            Content = BuildUserMessageContent(text, requestMode),
        });
        TouchActiveSession(text);
        StateHasChanged();
        await SaveAsync();

        try
        {
            var intent = HasImageInputs()
                ? new PromptIntentResult(true, "用户已添加图片附件")
                : await ChatClient.AnalyzeIntentAsync(Settings, text, _cts.Token);
            if (intent.Image)
            {
                ActiveSession.Prompt = text;
                var polishMode = StudioSettings.NormalizePromptPolishMode(Settings.PromptPolishMode);
                if (polishMode == "auto")
                {
                    _pendingPrompt = new PendingPrompt(text, string.Empty);
                    _loading = false;
                    await GenerateWithPolish();
                    return;
                }

                if (polishMode == "ask")
                {
                    var message = new StudioMessage
                    {
                        Role = PromptConfirmRole,
                        Content = "要先帮你润色一下提示词，再生成图片吗？",
                    };
                    ActiveSession.Messages.Add(message);
                    _pendingPrompt = new PendingPrompt(text, message.Id);
                    TouchActiveSession(text);
                    return;
                }

                _pendingPrompt = new PendingPrompt(text, string.Empty);
                _loading = false;
                await GenerateWithoutPolish();
                return;
            }

            var reply = await ChatClient.ReplyAsync(Settings, ActiveSession.Messages.SkipLast(1).ToArray(), text, _cts.Token);
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "assistant",
                Content = string.IsNullOrWhiteSpace(reply) ? "我在。你可以直接告诉我想生成什么画面。" : reply,
            });
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = "本次请求已取消。",
            });
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = $"会话失败：{ex.Message}",
            });
        }
        finally
        {
            _loading = false;
            _loadingLabel = "正在请求图像接口";
            TouchActiveSession();
            await SaveAsync();
            StateHasChanged();
        }
    }

    private async Task GenerateWithPolish()
    {
        if (_pendingPrompt is null || _loading)
        {
            return;
        }

        var original = _pendingPrompt.Text;
        _loading = true;
        _loadingLabel = "正在润色提示词";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        StateHasChanged();

        try
        {
            var polished = await ChatClient.PolishPromptAsync(Settings, original, _cts.Token);
            RemovePendingPromptMessages();
            _pendingPrompt = null;
            ActiveSession.Prompt = polished;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "assistant",
                Content = $"已润色提示词：{polished}",
            });
            await SaveAsync();
            _loading = false;
            await GenerateAsync(polished, addUserMessage: false);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            _loading = false;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = "本次润色已取消。",
            });
        }
        catch (Exception ex)
        {
            _loading = false;
            _error = ex.Message;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = $"润色失败：{ex.Message}",
            });
        }
        finally
        {
            _loadingLabel = "正在请求图像接口";
            TouchActiveSession();
            await SaveAsync();
            StateHasChanged();
        }
    }

    private async Task GenerateWithPolishAndRemember()
    {
        Settings.PromptPolishMode = "auto";
        await SaveAsync();
        await GenerateWithPolish();
    }

    private async Task GenerateWithoutPolish()
    {
        if (_pendingPrompt is null || _loading)
        {
            return;
        }

        var prompt = _pendingPrompt.Text;
        RemovePendingPromptMessages();
        _pendingPrompt = null;
        ActiveSession.Prompt = prompt;
        await GenerateAsync(prompt, addUserMessage: false);
    }

    private async Task GenerateWithoutPolishAndRemember()
    {
        Settings.PromptPolishMode = "direct";
        await SaveAsync();
        await GenerateWithoutPolish();
    }

    private void RemovePendingPromptMessages()
    {
        ActiveSession.Messages.RemoveAll(message => message.Role == PromptConfirmRole);
    }

    private void Cancel()
    {
        _cts?.Cancel();
    }

    private async Task DownloadImage(GeneratedImage image)
    {
        if (string.IsNullOrWhiteSpace(image.Url))
        {
            ClearDownloadNotice();
            _error = "没有可下载的图片地址。";
            return;
        }

        var fileName = BuildDownloadFileName(image);
        try
        {
            var result = await JsRuntime.InvokeAsync<ImageDownloadResult>("imageStudio.download", image.Url, fileName);
            _downloadNotice = BuildDownloadNotice(result);
            _lastDownloadFilePath = result?.SavedLocally == true ? result.FilePath : null;
            _error = null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            ClearDownloadNotice();
            _error = $"下载失败：{TrimJsError(ex.Message)}";
        }
    }

    private async Task RevealLastDownload()
    {
        if (string.IsNullOrWhiteSpace(_lastDownloadFilePath) || _revealDownloadBusy)
        {
            return;
        }

        _revealDownloadBusy = true;
        try
        {
            await JsRuntime.InvokeVoidAsync("imageStudio.revealFile", _lastDownloadFilePath);
            _error = null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            _error = $"无法打开文件位置：{TrimJsError(ex.Message)}";
        }
        finally
        {
            _revealDownloadBusy = false;
        }
    }

    private Task DownloadPreviewImage()
    {
        return _previewImage is null ? Task.CompletedTask : DownloadImage(_previewImage);
    }

    private void OpenImagePreview(GeneratedImage image, string alt)
    {
        if (string.IsNullOrWhiteSpace(image.Url))
        {
            return;
        }

        _previewImage = image;
        _previewAlt = alt;
        _previewZoomed = false;
        ResetImagePreviewEditState();
        ClearDownloadNotice();
    }

    private void CloseImagePreview()
    {
        _previewImage = null;
        _previewAlt = string.Empty;
        _previewZoomed = false;
        ResetImagePreviewEditState();
    }

    private void ToggleImagePreviewZoom()
    {
        _previewZoomed = !_previewZoomed;
        if (_previewEditing)
        {
            _previewMaskAttachPending = true;
            StateHasChanged();
        }
    }

    private async Task ToggleImagePreviewEdit()
    {
        if (_previewImage is null)
        {
            return;
        }

        _previewEditing = !_previewEditing;
        _previewZoomed = false;
        _previewEditError = null;

        if (_previewEditing)
        {
            _previewMaskAttachPending = true;
            StateHasChanged();
        }
    }

    private async Task OnPreviewImageLoaded()
    {
        if (_previewEditing)
        {
            await ResizePreviewMaskAsync();
        }
    }

    private async Task UpdatePreviewBrushSize(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var value))
        {
            _previewBrushSize = Math.Clamp(value, 12, 96);
            if (_previewEditing)
            {
                await InvokePreviewCanvasVoidAsync("setBrushSize", _previewBrushSize);
            }
        }
    }

    private void UpdatePreviewEditPrompt(ChangeEventArgs args)
    {
        _previewEditPrompt = args.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_previewEditPrompt))
        {
            _previewEditError = null;
        }
    }

    private async Task ClearPreviewMask()
    {
        _previewEditError = null;
        await InvokePreviewCanvasVoidAsync("clearMask");
    }

    private async Task SubmitPreviewEdit()
    {
        if (_previewImage is null || _loading)
        {
            return;
        }

        var prompt = _previewEditPrompt.Trim();
        if (prompt.Length == 0)
        {
            _previewEditError = "先写要修改什么。";
            return;
        }

        string? maskDataUrl = null;
        try
        {
            maskDataUrl = await JsRuntime.InvokeAsync<string?>("imageStudio.previewEditor.exportMask", _previewMaskCanvas);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            _previewEditError = $"无法读取标注区域：{TrimJsError(ex.Message)}";
            return;
        }

        if (string.IsNullOrWhiteSpace(maskDataUrl))
        {
            _previewEditError = "先在图片上标注要修改的区域。";
            return;
        }

        var imageUrl = _previewImage.Url;
        var editPrompt = ImageEditPromptBuilder.BuildMaskedRevisionPrompt(prompt, ActiveSession.Prompt);
        var userContent = ImageEditPromptBuilder.BuildMaskedRevisionUserMessage(prompt);

        CloseImagePreview();
        await GenerateImageRevisionAsync(imageUrl, editPrompt, userContent, maskDataUrl);
    }

    private async Task GenerateImageRevisionAsync(string imageUrl, string prompt, string userContent, string? maskDataUrl = null)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || _loading)
        {
            return;
        }

        ClearAllReferenceInputs();
        ActiveSession.ImageReferences = imageUrl;
        ActiveSession.MaskReference = maskDataUrl ?? string.Empty;
        ActiveSession.Prompt = prompt;
        ApplyResolvedMode("edit");
        var revisionMode = ResolveRequestMode(prompt, ActiveSession.Mode);
        ApplyResolvedMode(revisionMode);
        _senderText = string.Empty;
        ActiveSession.Messages.Add(new StudioMessage
        {
            Role = "user",
            Content = userContent,
        });
        TouchActiveSession(userContent);
        StateHasChanged();
        await SaveAsync();

        try
        {
            await GenerateAsync(prompt, addUserMessage: false);
        }
        finally
        {
            if (string.Equals(ActiveSession.ImageReferences, imageUrl, StringComparison.Ordinal) &&
                string.Equals(ActiveSession.MaskReference, maskDataUrl ?? string.Empty, StringComparison.Ordinal))
            {
                ActiveSession.ImageReferences = string.Empty;
                ActiveSession.MaskReference = string.Empty;
                ApplyResolvedMode("generate");
                TouchActiveSession();
                await SaveAsync();
                StateHasChanged();
            }
        }
    }

    private async Task ResizePreviewMaskAsync()
    {
        if (!_previewEditing)
        {
            return;
        }

        await InvokePreviewCanvasVoidAsync("attach", _previewMaskCanvas, _previewImageElement, _previewBrushSize);
    }

    private async Task InvokePreviewCanvasVoidAsync(string command, params object?[] args)
    {
        try
        {
            var jsArgs = string.Equals(command, "attach", StringComparison.Ordinal)
                ? args
                : new object?[] { _previewMaskCanvas }.Concat(args).ToArray();
            await JsRuntime.InvokeVoidAsync($"imageStudio.previewEditor.{command}", jsArgs);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            _previewEditError = $"标注工具不可用：{TrimJsError(ex.Message)}";
        }
    }

    private void ResetImagePreviewEditState()
    {
        _previewEditing = false;
        _previewMaskAttachPending = false;
        _previewEditPrompt = string.Empty;
        _previewEditError = null;
    }

    private string BuildDownloadFileName(GeneratedImage image)
    {
        var extension = ExtensionFromImage(image);
        return $"IoTCoWork-image-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{extension}";
    }

    private string ExtensionFromImage(GeneratedImage image)
    {
        var contentType = image.MimeType;
        if (string.IsNullOrWhiteSpace(contentType) &&
            image.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var metadataEnd = image.Url.IndexOfAny([';', ',']);
            if (metadataEnd > 5)
            {
                contentType = image.Url[5..metadataEnd];
            }
        }

        var extension = contentType?.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/webp" => "webp",
            "image/gif" => "gif",
            "image/svg+xml" => "svg",
            "image/bmp" => "bmp",
            "image/png" => "png",
            _ => Settings.Format,
        };

        extension = extension.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? "png" : extension;
    }

    private static string? BuildDownloadNotice(ImageDownloadResult? result)
    {
        if (result?.SavedLocally == true)
        {
            return string.IsNullOrWhiteSpace(result.FilePath)
                ? "图片已保存到下载目录。"
                : $"图片已保存：{result.FilePath}";
        }

        return "已开始下载图片。";
    }

    private void ClearDownloadNotice()
    {
        _downloadNotice = null;
        _lastDownloadFilePath = null;
        _revealDownloadBusy = false;
    }

    private static string TrimJsError(string message)
    {
        const string errorPrefix = "Error: ";
        return message.StartsWith(errorPrefix, StringComparison.Ordinal)
            ? message[errorPrefix.Length..]
            : message;
    }

    private Task MinimizeWindowAsync() => InvokeWindowCommandAsync("minimize");

    private Task ToggleMaximizeWindowAsync() => InvokeWindowCommandAsync("maximize");

    private void CloseWindowAsync()
    {
        _exitConfirmOpen = true;
    }

    private void CancelExit()
    {
        _exitConfirmOpen = false;
    }

    private async Task ConfirmExitAsync()
    {
        _exitConfirmOpen = false;
        await InvokeWindowCommandAsync("exit");
    }

    private async Task InvokeWindowCommandAsync(string command)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("imageStudio.window.invoke", command);
        }
        catch (JSException)
        {
        }
    }

    private ValueTask<bool> BeforeSenderUpload(IBrowserFile file)
    {
        var accepted = IsBrowserImageFile(file);
        if (!accepted)
        {
            _error = "目前只支持图片附件。";
        }

        return ValueTask.FromResult(accepted);
    }

    private async Task AddSenderFiles(IReadOnlyList<IBrowserFile> files)
    {
        const long maxFileSize = 12 * 1024 * 1024;
        _error = null;

        foreach (var file in files.Where(IsBrowserImageFile).Take(Math.Max(0, 16 - _senderImageAttachments.Count)))
        {
            await using var stream = file.OpenReadStream(maxFileSize);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var content = memory.ToArray();
            var reference = new ImageReferenceFile(file.Name, file.ContentType, content);
            var attachment = new XAttachmentItem
            {
                Name = file.Name,
                Description = "图片附件",
                ContentType = file.ContentType,
                Size = file.Size,
                ImageUrl = ToDataUrl(reference),
                Status = XFileCardStatus.Done,
            };

            _senderImageAttachments.Add(new SenderImageAttachment(attachment.Id, reference, attachment));
        }

        SyncSenderAttachments();
        SyncReferenceFilesFromSenderAttachments();
        ApplyResolvedMode(ResolveRequestMode(_senderText ?? string.Empty, ActiveSession.Mode));
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task RemoveSenderAttachment(string id)
    {
        _senderImageAttachments.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        SyncSenderAttachments();
        SyncReferenceFilesFromSenderAttachments();
        ApplyResolvedMode(ResolveRequestMode(_senderText ?? string.Empty, ActiveSession.Mode));
        TouchActiveSession();
        await SaveAsync();
    }

    private void SyncSenderAttachments()
    {
        _senderAttachments = _senderImageAttachments.Select(item => item.Attachment).ToArray();
    }

    private void SyncReferenceFilesFromSenderAttachments()
    {
        if (_senderImageAttachments.Count == 0)
        {
            if (_referenceFiles.Count > 0 && ActiveSession.ImageReferences.Contains("local:", StringComparison.OrdinalIgnoreCase))
            {
                _referenceFiles.Clear();
                ActiveSession.ImageReferences = string.Empty;
            }

            return;
        }

        _referenceFiles.Clear();
        _referenceFiles.AddRange(_senderImageAttachments.Select(item => item.ReferenceFile));
        ActiveSession.ImageReferences = string.Join('\n', _referenceFiles.Select(file => $"local:{file.FileName}"));
    }

    private void ClearSenderAttachments()
    {
        _senderImageAttachments.Clear();
        SyncSenderAttachments();
        if (_referenceFiles.Count > 0 && ActiveSession.ImageReferences.Contains("local:", StringComparison.OrdinalIgnoreCase))
        {
            _referenceFiles.Clear();
            ActiveSession.ImageReferences = string.Empty;
        }
    }

    private void ClearAllReferenceInputs()
    {
        _referenceFiles.Clear();
        _senderImageAttachments.Clear();
        SyncSenderAttachments();
    }

    private static string ToDataUrl(ImageReferenceFile file)
    {
        var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType.Trim();
        return $"data:{mimeType};base64,{Convert.ToBase64String(file.Content)}";
    }

    private async Task<IReadOnlyList<GeneratedImage>> PersistGeneratedImagesAsync(
        IReadOnlyList<GeneratedImage> images,
        CancellationToken cancellationToken)
    {
        var persisted = new List<GeneratedImage>(images.Count);
        foreach (var image in images)
        {
            persisted.Add(new GeneratedImage
            {
                Url = await PersistImageUrlAsync(image.Url, cancellationToken),
                RevisedPrompt = image.RevisedPrompt,
                MimeType = image.MimeType,
            });
        }

        return persisted;
    }

    private async Task<string> PersistImageUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        try
        {
            var response = await Http.GetFromJsonAsync<PersistedImageResponse>(
                $"api/local/image-data?url={Uri.EscapeDataString(url)}",
                cancellationToken);

            return string.IsNullOrWhiteSpace(response?.DataUrl) ? url : response.DataUrl;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return url;
        }
    }

    private async Task NewSession()
    {
        var session = new StudioSession
        {
            Title = $"新建作图 {_snapshot.Sessions.Count + 1}",
        };
        _snapshot.Sessions.Insert(0, session);
        _snapshot.ActiveSessionId = session.Id;
        ClearDownloadNotice();
        ClearAllReferenceInputs();
        await SaveAsync();
    }

    private async Task ActivateSession(string key)
    {
        _cts?.Cancel();
        ClearAllReferenceInputs();
        _snapshot.ActiveSessionId = key;
        _pendingPrompt = null;
        EnsureActiveMode();
        _error = null;
        ClearDownloadNotice();
        await SaveAsync();
    }

    private async Task RenameSession(XConversationRenameRequest request)
    {
        var session = _snapshot.Sessions.FirstOrDefault(item => item.Id == request.Key);
        if (session is null)
        {
            return;
        }

        session.Title = request.Title.Trim();
        TouchSession(session);
        await SaveAsync();
    }

    private async Task DeleteSession(XConversationItem item)
    {
        if (_snapshot.Sessions.Count <= 1)
        {
            ResetActive();
            await SaveAsync();
            return;
        }

        _snapshot.Sessions.RemoveAll(session => session.Id == item.Key);
        if (_snapshot.ActiveSessionId == item.Key)
        {
            _snapshot.ActiveSessionId = _snapshot.Sessions.OrderByDescending(session => session.UpdatedAt).First().Id;
        }

        await SaveAsync();
    }

    private async Task ClearHistory()
    {
        var session = new StudioSession();
        _snapshot.Sessions = [session];
        _snapshot.ActiveSessionId = session.Id;
        _pendingPrompt = null;
        ClearDownloadNotice();
        ClearAllReferenceInputs();
        await SaveAsync();
    }

    private async Task ClearResults()
    {
        ActiveSession.Messages.RemoveAll(message => message.Images.Count > 0);
        ClearDownloadNotice();
        CloseImagePreview();
        TouchActiveSession();
        await SaveAsync();
    }

    private void ResetActive()
    {
        ActiveSession.Prompt = string.Empty;
        ActiveSession.ImageReferences = string.Empty;
        ActiveSession.Messages.Clear();
        _pendingPrompt = null;
        ClearDownloadNotice();
        CloseImagePreview();
        ClearAllReferenceInputs();
        TouchActiveSession("新建作图");
    }

    private async Task UsePrompt(string prompt)
    {
        ActiveSession.Prompt = prompt;
        _senderText = prompt;
        TouchActiveSession(prompt);
        await SaveAsync();
    }


    private IReadOnlyList<string> ParseReferences()
    {
        return ActiveSession.ImageReferences
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(reference => !reference.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private string ResolveRequestMode(string prompt, string currentMode)
    {
        var normalizedMode = NormalizeMode(currentMode);
        if (!HasImageInputs())
        {
            return normalizedMode;
        }

        if (LooksLikeEditIntent(prompt) || !string.IsNullOrWhiteSpace(ActiveSession.MaskReference))
        {
            return "edit";
        }

        if (normalizedMode == "variation")
        {
            return string.IsNullOrWhiteSpace(prompt) ? "variation" : "image";
        }

        return normalizedMode == "edit" ? "edit" : "image";
    }

    private void ApplyResolvedMode(string mode)
    {
        mode = NormalizeMode(mode);
        ActiveSession.Mode = mode;
        if (mode == "variation")
        {
            Settings.Model = "gpt-image-2";
            Settings.RequestMode = "sync";
            if (_referenceFiles.Count > 1)
            {
                var first = _referenceFiles[0];
                _referenceFiles.Clear();
                _referenceFiles.Add(first);
                ActiveSession.ImageReferences = $"local:{first.FileName}";
            }

            if (_senderImageAttachments.Count > 1)
            {
                var firstAttachment = _senderImageAttachments[0];
                _senderImageAttachments.Clear();
                _senderImageAttachments.Add(firstAttachment);
                SyncSenderAttachments();
            }
        }

        CoerceImageSettingsForCurrentModel();
    }

    private bool HasImageInputs()
    {
        return _referenceFiles.Count > 0 ||
            ParseReferences().Count > 0;
    }

    private static string NormalizeMode(string? mode)
    {
        return mode switch
        {
            "image" => "image",
            "edit" => "edit",
            "variation" => "variation",
            _ => "generate",
        };
    }

    private static bool LooksLikeEditIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return ContainsAny(text,
            "编辑", "修改", "改一下", "改成", "替换", "换成", "换背景", "去掉", "移除", "擦除",
            "修复", "不对劲", "不自然", "有问题", "变形", "畸形", "补全", "扩图", "局部", "遮罩",
            "mask", "edit", "remove", "replace", "inpaint", "outpaint");
    }

    private bool TryCreateLatestImageRevision(string text, out ImageRevisionRequest revision)
    {
        revision = default!;
        if (!LooksLikeImageRevisionFeedback(text))
        {
            return false;
        }

        var image = ActiveSession.Messages
            .LastOrDefault(message => message.Images.Count > 0)?
            .Images
            .LastOrDefault();
        if (image is null || string.IsNullOrWhiteSpace(image.Url))
        {
            return false;
        }

        revision = new ImageRevisionRequest(image.Url, BuildImageRevisionPrompt(text, ActiveSession.Prompt));
        return true;
    }

    private static bool LooksLikeImageRevisionFeedback(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ContainsAny(text,
            "不对劲", "明显不对", "不对", "有问题", "不自然", "怪", "很怪", "奇怪", "错误", "错了",
            "畸形", "变形", "扭曲", "崩", "糊了", "模糊", "穿帮", "多指", "少指", "断指", "粘连"))
        {
            return true;
        }

        return ContainsAny(text, "手", "手指", "手掌", "胳膊", "手臂", "脸", "眼睛", "腿", "脚", "身体", "姿势") &&
            ContainsAny(text, "修", "修复", "改", "改一下", "调整", "重新", "再来", "重画", "优化", "不好", "不行");
    }

    private static string BuildImageRevisionPrompt(string feedback, string previousPrompt)
    {
        var trimmedFeedback = feedback.Trim();
        var priorPromptSection = string.IsNullOrWhiteSpace(previousPrompt)
            ? string.Empty
            : $"""

            上一轮提示词：{previousPrompt.Trim()}
            """;

        return $"""
            请以上一张图作为参考，重新生成整张图。保留原图的主体身份、脸部特征、服装、姿势、背景、灯光、构图、镜头语言和整体风格，只修正用户指出的问题。

            用户反馈：{trimmedFeedback}
            {priorPromptSection}

            正向提示词：重点修复画面中不自然或错误的部位，尤其是手部结构；手指数量正确，手掌比例合理，关节清晰，手腕与手臂连接自然，皮肤纹理、肤色和光影与原图一致。保持人物完整、边缘干净、细节清晰、画面自然真实。

            负面提示词：畸形手，多指，少指，断指，粘连手指，扭曲手掌，错误关节，手指过长，手指过短，手腕断裂，不自然姿势，模糊手部，脸部变形，五官错位，身体比例错误，肢体扭曲，改变人物身份，改变服装，改变背景，过度修饰，低质量，变形，失真。
            """;
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBrowserImageFile(IBrowserFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.ContentType) &&
            file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = file.Name;
        return name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildUserMessageContent(string text, string requestMode)
    {
        var content = string.IsNullOrWhiteSpace(text) ? ModeName(requestMode) : text;
        var inputCount = _referenceFiles.Count + ParseReferences().Count;
        return HasImageInputs()
            ? $"{content}\n\n已添加 {inputCount} 张图片附件，模式：{ModeName(requestMode)}。"
            : content;
    }

    private StudioSettings CreateRequestSettings()
    {
        CoerceImageSettingsForCurrentModel();
        Settings.AspectRatio = EffectiveAspectRatio;
        Settings.ResolutionTier = StudioSettings.NormalizeResolutionTier(Settings.ResolutionTier);
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        ImageModelCatalog.NormalizeSettings(Settings, ActiveSession.Mode);
        return Settings;
    }

    private void TouchActiveSession(string? titleSeed = null)
    {
        TouchSession(ActiveSession, titleSeed);
    }

    private static void TouchSession(StudioSession session, string? titleSeed = null)
    {
        session.UpdatedAt = DateTimeOffset.Now;
        if (!string.IsNullOrWhiteSpace(titleSeed))
        {
            session.Title = titleSeed.Length > 24 ? titleSeed[..24] + "..." : titleSeed;
        }
    }

    private async Task SaveAsync()
    {
        await Storage.SaveAsync(_snapshot);
    }

    private async Task SyncDocumentThemeAsync()
    {
        var effectiveTheme = EffectiveTheme;
        if (string.Equals(_lastDocumentTheme, effectiveTheme, StringComparison.Ordinal))
        {
            return;
        }

        _lastDocumentTheme = effectiveTheme;
        try
        {
            await JsRuntime.InvokeVoidAsync("imageStudio.setDocumentTheme", effectiveTheme);
        }
        catch (JSException)
        {
        }
    }

    private static string ResolveAccountInitials(string displayName, string? email)
    {
        var source = !string.IsNullOrWhiteSpace(displayName) && displayName != "未登录"
            ? displayName
            : email;
        source = source?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            return "未";
        }

        return source[..1].ToUpperInvariant();
    }

    private string ResolveAccountDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(Settings.SaaSUser?.DisplayName))
        {
            return Settings.SaaSUser.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(Settings.SaaSUser?.Email))
        {
            return Settings.SaaSUser.Email.Trim();
        }

        return "未登录";
    }

    private string AccountThemeChecked(string mode) =>
        string.Equals(Settings.ThemeMode, mode, StringComparison.OrdinalIgnoreCase) ? "true" : "false";

    private void ToggleAccountMenu()
    {
        _accountMenuOpen = !_accountMenuOpen;
    }

    private void CloseAccountMenu()
    {
        _accountMenuOpen = false;
    }

    private void HandleAccountMenuKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseAccountMenu();
        }
    }

    private void HandleAccountMenuButtonKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "ArrowDown")
        {
            _accountMenuOpen = true;
        }
    }

    private void OpenSettings() => _settingsOpen = true;
    private void CloseSettings() => _settingsOpen = false;
    private void OpenCapabilityCenter()
    {
        CloseAccountMenu();
        _capabilityCenterOpen = true;
    }

    private void CloseCapabilityCenter()
    {
        _capabilityCenterOpen = false;
    }

    private void OpenCapabilityCenterFromSettings()
    {
        _settingsOpen = false;
        OpenCapabilityCenter();
    }

    private void OpenSettingsFromAccountMenu()
    {
        CloseAccountMenu();
        OpenSettings();
    }

    private async Task SignOutFromAccountMenu()
    {
        CloseAccountMenu();
        await SignOutAccount();
    }

    private void OpenAuthProxySettings() => _authProxyOpen = true;
    private void CloseAuthProxySettings() => _authProxyOpen = false;

    private async Task CheckForUpdatesAsync()
    {
        await CheckForUpdatesAsync(silent: false);
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_updateChecking)
        {
            return;
        }

        _updateChecking = true;
        _updateNotice = silent ? _updateNotice : null;
        _updateNoticeIsError = false;
        if (!silent)
        {
            StateHasChanged();
        }

        try
        {
            var info = await UpdateClient.CheckAsync();
            if (info is null)
            {
                _updateNotice = silent
                    ? null
                    : "当前运行环境没有本地更新服务，无法检查 GitHub Release。";
                _updateNoticeIsError = !silent;
                return;
            }

            _updateInfo = info;
            _updateNotice = info.Message;
            _updateNoticeIsError = !info.Supported && !silent;
        }
        catch (Exception ex)
        {
            _updateNotice = silent ? null : $"检查更新失败：{ex.Message}";
            _updateNoticeIsError = !silent;
        }
        finally
        {
            _updateChecking = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_updateInstalling || _updateInfo?.CanInstall != true)
        {
            return;
        }

        _updateInstalling = true;
        _updateNotice = "正在下载更新包，完成后 IoTCoWork 会自动重启。";
        _updateNoticeIsError = false;
        StateHasChanged();

        try
        {
            var result = await UpdateClient.InstallAsync(_updateInfo);
            _updateNotice = result.Message;
            _updateNoticeIsError = !string.Equals(result.Status, "installing", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _updateNotice = $"安装更新失败：{ex.Message}";
            _updateNoticeIsError = true;
        }
        finally
        {
            _updateInstalling = false;
            StateHasChanged();
        }
    }

    private void ToggleAccountRegister()
    {
        _accountRegisterOpen = !_accountRegisterOpen;
    }

    private void SetAuthMode(bool register)
    {
        _accountRegisterOpen = register;
        _accountMessage = null;
        _accountMessageIsError = false;
    }

    private async Task LoginAccount()
    {
        await RunAccountAction(async () =>
        {
            var response = await AccountClient.LoginAsync(Settings, _accountEmail, _accountPassword);
            await CompleteAccountSetupAsync();
            await SaveAsync();
            _accountPassword = string.Empty;
            _settingsOpen = false;
            SetAccountMessage($"已登录 {response.User?.Email ?? _accountEmail}。");
        });
    }

    private async Task RegisterAccount()
    {
        await RunAccountAction(async () =>
        {
            var response = await AccountClient.RegisterAsync(
                Settings,
                _accountEmail,
                _accountPassword,
                promoCode: null,
                invitationCode: null,
                affiliateCode: null);
            await CompleteAccountSetupAsync();
            await SaveAsync();
            _accountPassword = string.Empty;
            _accountRegisterOpen = false;
            _settingsOpen = false;
            SetAccountMessage($"已注册并登录 {response.User?.Email ?? _accountEmail}。");
        });
    }

    private Task SubmitAuth()
    {
        return _accountRegisterOpen ? RegisterAccount() : LoginAccount();
    }

    private async Task RefreshAccountProfile()
    {
        await RunAccountAction(async () =>
        {
            var user = await AccountClient.RefreshProfileAsync(Settings);
            await SaveAsync();
            SetAccountMessage($"账户已刷新，余额 {user.Balance:0.####}。");
        });
    }

    private async Task CreateRechargeOrder()
    {
        await RunAccountAction(async () =>
        {
            var order = await AccountClient.CreateRechargeOrderAsync(
                Settings,
                _rechargeAmount,
                "native");
            await SaveAsync();
            OpenPaymentOverlay(order);
            SetAccountMessage("充值订单已创建，请扫码完成支付。");
        });
    }

    private async Task SignOutAccount()
    {
        await StopPaymentPollingAsync();
        _paymentOverlayOpen = false;
        _activePaymentOrder = null;
        _latestPaymentStatus = null;
        _paymentQrImageSource = null;
        AccountClient.SignOut(Settings);
        await SaveAsync();
        _settingsOpen = false;
        SetAccountMessage("已退出登录。");
    }

    private async Task RunAccountAction(Func<Task> action)
    {
        _accountBusy = true;
        _accountMessage = null;
        _accountMessageIsError = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetAccountMessage(ex.Message, isError: true);
        }
        finally
        {
            _accountBusy = false;
            StateHasChanged();
        }
    }

    private void SetAccountMessage(string message, bool isError = false)
    {
        _accountMessage = message;
        _accountMessageIsError = isError;
    }

    private void StartServerStatusPolling()
    {
        _serverStatusCts?.Cancel();
        _serverStatusTimer?.Dispose();
        _serverStatusCts?.Dispose();

        _serverStatusCts = new CancellationTokenSource();
        _serverStatusTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        _ = PollServerStatusLoopAsync(_serverStatusCts.Token);
    }

    private async Task PollServerStatusLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckServerStatusAsync(cancellationToken);
            await InvokeAsync(StateHasChanged);

            while (_serverStatusTimer is not null &&
                await _serverStatusTimer.WaitForNextTickAsync(cancellationToken))
            {
                await CheckServerStatusAsync(cancellationToken);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckServerStatusAsync(CancellationToken cancellationToken)
    {
        _serverStatusChecked = true;
        _serverStatusCheckedAt = DateTimeOffset.Now;

        try
        {
            using var response = await Http.GetAsync("api/local/health", cancellationToken);
            _serverOnline = response.IsSuccessStatusCode;
        }
        catch
        {
            _serverOnline = false;
        }
    }

    private async Task RestoreAccountAsync()
    {
        if (!AccountLoggedIn)
        {
            var hadStoredCloudToken = !string.IsNullOrWhiteSpace(Settings.CloudAccessToken);
            Settings.CloudAccessToken = string.Empty;
            Settings.CloudRefreshToken = string.Empty;
            Settings.CloudTokenExpiresAt = null;
            if (hadStoredCloudToken)
            {
                await SaveAsync();
            }
            return;
        }

        _accountBusy = true;
        _accountMessage = null;
        _accountMessageIsError = false;
        try
        {
            await CompleteAccountSetupAsync();
            await SaveAsync();
            SetAccountMessage("账户已恢复。");
        }
        catch (Exception ex)
        {
            AccountClient.SignOut(Settings);
            await SaveAsync();
            SetAccountMessage($"登录状态已失效，请重新登录。{ex.Message}", isError: true);
        }
        finally
        {
            _accountBusy = false;
        }
    }

    private async Task CompleteAccountSetupAsync()
    {
        await AccountClient.RefreshProfileAsync(Settings);
        await AccountClient.EnsureCloudTokenAsync(Settings);
    }

    private void OpenPaymentOverlay(SaaSRechargeOrder order)
    {
        _activePaymentOrder = order;
        _latestPaymentStatus = null;
        _paymentExpiresAt = order.ExpiresAt == default
            ? DateTimeOffset.Now.AddMinutes(30)
            : order.ExpiresAt;
        _paymentQrImageSource = BuildPaymentQrImageSource(order);
        _paymentOverlayOpen = true;
        _settingsOpen = false;
        StartPaymentPolling();
    }

    private void ShowPendingPaymentOrder()
    {
        _paymentOverlayOpen = true;
        _settingsOpen = false;
        if (_paymentTimer is null)
        {
            StartPaymentPolling();
        }
    }

    private void StartPaymentPolling()
    {
        _ = StopPaymentPollingAsync();
        if (_activePaymentOrder is null)
        {
            return;
        }

        _paymentCts = new CancellationTokenSource();
        _paymentTimer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        _ = PollPaymentLoopAsync(_paymentCts.Token);
    }

    private async Task PollPaymentLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshPaymentStatusAsync();

            while (_paymentTimer is not null &&
                await _paymentTimer.WaitForNextTickAsync(cancellationToken))
            {
                if (_activePaymentOrder is null || PaymentSucceeded || PaymentExpired)
                {
                    break;
                }

                await RefreshPaymentStatusAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task VerifyPaymentNow()
    {
        await UpdatePaymentStatusAsync(activeVerify: true, silent: false);
        StateHasChanged();
    }

    private async Task RefreshPaymentStatusAsync()
    {
        await UpdatePaymentStatusAsync(activeVerify: false, silent: true);
    }

    private async Task UpdatePaymentStatusAsync(bool activeVerify, bool silent)
    {
        if (_activePaymentOrder is null || _paymentPolling)
        {
            return;
        }

        _paymentPolling = true;
        try
        {
            _latestPaymentStatus = activeVerify
                ? await AccountClient.VerifyPaymentOrderAsync(Settings, _activePaymentOrder.OrderNo)
                : await AccountClient.GetPaymentOrderAsync(Settings, _activePaymentOrder.OrderNo);

            if (PaymentSucceeded)
            {
                await AccountClient.RefreshProfileAsync(Settings);
                await SaveAsync();
                SetAccountMessage("充值已完成，余额已刷新。");
                await StopPaymentPollingAsync();
            }
            else if (PaymentExpired)
            {
                SetAccountMessage("订单已过期，请重新生成二维码。", isError: true);
                await StopPaymentPollingAsync();
            }
            else if (!silent)
            {
                SetAccountMessage("还没有确认到账，请稍后再试。");
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                SetAccountMessage(ex.Message, isError: true);
            }
        }
        finally
        {
            _paymentPolling = false;
        }
    }

    private async Task CancelPaymentOrder()
    {
        if (_activePaymentOrder is null || _paymentCancelling)
        {
            return;
        }

        _paymentCancelling = true;
        try
        {
            await AccountClient.CancelPaymentOrderAsync(Settings, _activePaymentOrder.OrderNo);
            await StopPaymentPollingAsync();
            _paymentOverlayOpen = false;
            _activePaymentOrder = null;
            _latestPaymentStatus = null;
            _paymentQrImageSource = null;
            SetAccountMessage("充值订单已取消。");
        }
        catch (Exception ex)
        {
            SetAccountMessage(ex.Message, isError: true);
        }
        finally
        {
            _paymentCancelling = false;
        }
    }

    private async Task ClosePaymentOverlay()
    {
        if (PaymentSucceeded || PaymentExpired)
        {
            await StopPaymentPollingAsync();
        }

        _paymentOverlayOpen = false;
    }

    private async Task StopPaymentPollingAsync()
    {
        _paymentCts?.Cancel();
        _paymentTimer?.Dispose();
        _paymentTimer = null;
        _paymentCts?.Dispose();
        _paymentCts = null;
        await Task.CompletedTask;
    }

    private static string? BuildPaymentQrImageSource(SaaSRechargeOrder order)
    {
        var qrValue = !string.IsNullOrWhiteSpace(order.QrCode)
            ? order.QrCode.Trim()
            : order.PayUrl?.Trim();
        if (string.IsNullOrWhiteSpace(qrValue))
        {
            return null;
        }

        if (qrValue.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return qrValue;
        }

        if (IsImageUrl(qrValue))
        {
            return qrValue;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(qrValue, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    private static bool IsImageUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPaymentSuccess(string? status)
    {
        return string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPaymentExpired(string? status)
    {
        return status is not null &&
            (status.Equals("closed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("refunded", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatSignedMoney(decimal value)
    {
        return value >= 0 ? $"+{FormatMoney(value)}" : FormatMoney(value);
    }

    private static string FormatSignedNumber(decimal value)
    {
        return value >= 0 ? $"+{FormatMoney(value)}" : FormatMoney(value);
    }

    private sealed record AspectRatioPreset(string Ratio, string? Badge = null)
    {
        public string Label => Ratio;
    }

    private sealed record ResolutionPreset(string Tier, string Label, string Description);
    private sealed record PendingPrompt(string Text, string MessageId);
    private sealed record ImageRevisionRequest(string ImageUrl, string Prompt);
    private sealed record PromptPolishOption(string Value, string Label, string Description);
    private sealed record SenderImageAttachment(string Id, ImageReferenceFile ReferenceFile, XAttachmentItem Attachment);

    private sealed class ImageDownloadResult
    {
        public bool SavedLocally { get; set; }
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
    }

    private static string FormatMoney(decimal value)
    {
        return value == decimal.Truncate(value)
            ? value.ToString("0")
            : value.ToString("0.####");
    }

    private async Task UpdateMode(string mode)
    {
        ApplyResolvedMode(ResolveRequestMode(_senderText ?? string.Empty, mode));

        TouchActiveSession();
        await SaveAsync();
    }

    private async Task UpdateReferences(ChangeEventArgs args)
    {
        ActiveSession.ImageReferences = args.Value?.ToString() ?? string.Empty;
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task UpdateMaskReference(ChangeEventArgs args)
    {
        ActiveSession.MaskReference = args.Value?.ToString() ?? string.Empty;
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task AddReferenceFiles(InputFileChangeEventArgs args)
    {
        const long maxFileSize = 12 * 1024 * 1024;
        ClearAllReferenceInputs();
        var maxFiles = ActiveSession.Mode == "variation" ? 1 : 16;
        foreach (var file in args.GetMultipleFiles(maxFiles))
        {
            await using var stream = file.OpenReadStream(maxFileSize);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var reference = new ImageReferenceFile(file.Name, file.ContentType, memory.ToArray());
            _referenceFiles.Add(reference);
            var attachment = new XAttachmentItem
            {
                Name = file.Name,
                Description = "图片附件",
                ContentType = file.ContentType,
                Size = file.Size,
                ImageUrl = ToDataUrl(reference),
                Status = XFileCardStatus.Done,
            };
            _senderImageAttachments.Add(new SenderImageAttachment(attachment.Id, reference, attachment));
        }

        ActiveSession.ImageReferences = string.Join('\n', _referenceFiles.Select(file => $"local:{file.FileName}"));
        SyncSenderAttachments();
        ApplyResolvedMode(ResolveRequestMode(_senderText ?? string.Empty, ActiveSession.Mode));
        TouchActiveSession();
        await SaveAsync();
    }

    private void UpdateAccountEmail(ChangeEventArgs args) { _accountEmail = args.Value?.ToString() ?? string.Empty; }
    private void UpdateAccountPassword(ChangeEventArgs args) { _accountPassword = args.Value?.ToString() ?? string.Empty; }
    private async Task UpdateAccountProxyUrl(ChangeEventArgs args)
    {
        Settings.NetworkProxyUrl = NormalizeProxyUrl(args.Value?.ToString());
        await SaveAsync();
    }

    private void ToggleRatioMenu()
    {
        _ratioMenuOpen = !_ratioMenuOpen;
        if (_ratioMenuOpen)
        {
            _resolutionMenuOpen = false;
        }
    }

    private void ToggleResolutionMenu()
    {
        _resolutionMenuOpen = !_resolutionMenuOpen;
        if (_resolutionMenuOpen)
        {
            _ratioMenuOpen = false;
        }
    }

    private async Task SelectAspectRatio(string aspectRatio)
    {
        Settings.AspectRatio = StudioSettings.NormalizeAspectRatio(aspectRatio);
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        _ratioMenuOpen = false;
        await SaveAsync();
    }

    private async Task SelectResolution(string resolutionTier)
    {
        Settings.ResolutionTier = NormalizeResolutionTierForModel(resolutionTier, CurrentModelCapabilities);
        Settings.Size = BuildImageSize(EffectiveAspectRatio, Settings.ResolutionTier);
        CoerceImageSettingsForCurrentModel();
        _resolutionMenuOpen = false;
        await SaveAsync();
    }

    private async Task UpdateQuickQuality(ChangeEventArgs args)
    {
        Settings.Quality = args.Value?.ToString() ?? "auto";
        CoerceImageSettingsForCurrentModel();
        await SaveAsync();
    }

    private async Task UpdateQuickFormat(ChangeEventArgs args)
    {
        Settings.Format = args.Value?.ToString() ?? "png";
        CoerceImageSettingsForCurrentModel();
        await SaveAsync();
    }

    private async Task UpdateQuickCount(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var value))
        {
            _count = Math.Clamp(value, 1, 10);
            await SaveAsync();
        }
    }

    private async Task UpdateThemeMode(string mode)
    {
        Settings.ThemeMode = StudioSettings.NormalizeThemeMode(mode);
        await SyncDocumentThemeAsync();
        await SaveAsync();
    }

    private async Task UpdatePromptPolishMode(string mode)
    {
        Settings.PromptPolishMode = StudioSettings.NormalizePromptPolishMode(mode);
        if (Settings.PromptPolishMode != "ask")
        {
            _pendingPrompt = null;
            RemovePendingPromptMessages();
        }

        await SaveAsync();
    }

    [JSInvokable]
    public Task OnSystemThemeChanged(bool prefersDark)
    {
        if (_systemPrefersDark == prefersDark)
        {
            return Task.CompletedTask;
        }

        _systemPrefersDark = prefersDark;
        return InvokeAsync(StateHasChanged);
    }

    private void UpdateRechargeAmount(ChangeEventArgs args)
    {
        if (decimal.TryParse(args.Value?.ToString(), out var amount))
        {
            _rechargeAmount = Math.Max(1, amount);
        }
    }

    private async Task UpdateModel(string value)
    {
        Settings.Model = ImageModelCatalog.NormalizeModel(value);
        CoerceImageSettingsForCurrentModel();
        await SaveAsync();
    }
    private async Task UpdateCount(int value) { _count = Math.Clamp(value, 1, 10); await SaveAsync(); }
    private async Task UpdateSize(string value)
    {
        Settings.Size = string.IsNullOrWhiteSpace(value)
            ? BuildImageSize(EffectiveAspectRatio, Settings.ResolutionTier)
            : value;
        Settings.AspectRatio = ResolveAspectRatio(Settings.AspectRatio, Settings.Size);
        await SaveAsync();
    }
    private async Task UpdateQuality(string value) { Settings.Quality = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateStyle(string value) { Settings.Style = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateBackground(string value) { Settings.Background = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateFormat(string value) { Settings.Format = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateCompression(int value) { Settings.Compression = Math.Clamp(value, 0, 100); await SaveAsync(); }
    private async Task UpdateModeration(string value) { Settings.Moderation = value; await SaveAsync(); }
    private async Task UpdateFidelity(string value) { Settings.InputFidelity = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateUser(ChangeEventArgs args) { Settings.User = args.Value?.ToString() ?? string.Empty; await SaveAsync(); }
    private async Task UpdateRequestMode(ChangeEventArgs args) { Settings.RequestMode = args.Value?.ToString() ?? "stream"; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdatePartial(int value) { Settings.PartialImages = Math.Clamp(value, 0, 3); CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateAdvancedJson(ChangeEventArgs args) { Settings.AdvancedJson = args.Value?.ToString() ?? string.Empty; await SaveAsync(); }

    private static string NormalizeProxyUrl(string? value)
    {
        var proxy = value?.Trim() ?? string.Empty;
        if (proxy.Length == 0)
        {
            return string.Empty;
        }

        return proxy.Contains("://", StringComparison.Ordinal) ? proxy : $"http://{proxy}";
    }

    private void EnsureActiveMode()
    {
        if (string.IsNullOrWhiteSpace(ActiveSession.Mode) || ActiveSession.Mode == "text")
        {
            ActiveSession.Mode = "generate";
        }
    }

    private static string ModeName(string? mode)
    {
        return mode switch
        {
            "image" => "图生图",
            "edit" => "图片编辑",
            "variation" => "变化",
            _ => "文生图",
        };
    }

    public void Dispose()
    {
        if (!string.IsNullOrWhiteSpace(_systemThemeWatchId))
        {
            _ = JsRuntime.InvokeVoidAsync("imageStudio.unwatchSystemTheme", _systemThemeWatchId);
        }

        _selfReference?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _paymentCts?.Cancel();
        _paymentTimer?.Dispose();
        _paymentCts?.Dispose();
        _serverStatusCts?.Cancel();
        _serverStatusTimer?.Dispose();
        _serverStatusCts?.Dispose();
    }

    private sealed class PersistedImageResponse
    {
        public string DataUrl { get; set; } = string.Empty;
    }
}
