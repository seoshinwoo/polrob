namespace polrob.Client.Voice;

// HybridWebView의 마이크 요청을 각 운영체제 WebView에 연결합니다.
// OS 자체 마이크 권한은 HybridWebViewVoiceRoomClient가 별도로 요청합니다.
internal sealed class VoiceWebViewPlatformConfiguration
{
#if IOS
    private WebKit.WKUIDelegate? _iOSUiDelegate;
#endif

    public void Attach(HybridWebView webView)
    {
        webView.HandlerChanged += OnHandlerChanged;
        Configure(webView);
    }

    public void Detach(HybridWebView webView)
    {
        webView.HandlerChanged -= OnHandlerChanged;
    }

    private void OnHandlerChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is HybridWebView webView)
        {
            Configure(webView);
        }
    }

    private void Configure(HybridWebView webView)
    {
#if ANDROID
        if (webView.Handler?.PlatformView is Android.Webkit.WebView androidWebView)
        {
            // 게임 화면 진입 자체가 사용자의 동작이므로 구독 오디오가 즉시 재생되게 합니다.
            androidWebView.Settings.MediaPlaybackRequiresUserGesture = false;
            androidWebView.SetWebChromeClient(new VoiceWebChromeClient());
        }
#elif IOS
        if (webView.Handler?.PlatformView is WebKit.WKWebView iOSWebView)
        {
            _iOSUiDelegate = new VoiceWebViewUiDelegate();
            iOSWebView.UIDelegate = _iOSUiDelegate;
        }
#endif
    }

#if ANDROID
    private sealed class VoiceWebChromeClient : Android.Webkit.WebChromeClient
    {
        public override void OnPermissionRequest(Android.Webkit.PermissionRequest? request)
        {
            if (request == null)
            {
                return;
            }

            var resources = request.GetResources() ?? Array.Empty<string>();
            var requestsOnlyAudio = resources.Length > 0 &&
                                    resources.All(resource =>
                                        resource == Android.Webkit.PermissionRequest.ResourceAudioCapture);
            var isTrustedAppOrigin = request.Origin?.Scheme == "https" &&
                                     request.Origin.Host == "0.0.0.1";
            var hasOsPermission = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                Android.App.Application.Context,
                Android.Manifest.Permission.RecordAudio) ==
                Android.Content.PM.Permission.Granted;

            if (requestsOnlyAudio && isTrustedAppOrigin && hasOsPermission)
            {
                request.Grant(new[] { Android.Webkit.PermissionRequest.ResourceAudioCapture });
            }
            else
            {
                // 카메라 등 요청하지 않은 WebView 권한은 허용하지 않습니다.
                request.Deny();
            }
        }
    }
#elif IOS
    private sealed class VoiceWebViewUiDelegate : WebKit.WKUIDelegate
    {
        public override void RequestMediaCapturePermission(
            WebKit.WKWebView webView,
            WebKit.WKSecurityOrigin origin,
            WebKit.WKFrameInfo frame,
            WebKit.WKMediaCaptureType type,
            Action<WebKit.WKPermissionDecision> decisionHandler)
        {
            var isTrustedAppOrigin = origin.Protocol == "app" && origin.Host == "0.0.0.1";
            decisionHandler(isTrustedAppOrigin && type == WebKit.WKMediaCaptureType.Microphone
                ? WebKit.WKPermissionDecision.Grant
                : WebKit.WKPermissionDecision.Deny);
        }
    }
#endif
}
