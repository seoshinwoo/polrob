using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace polrob.Client.Voice;

// 게임 팀원 정보와 LiveKit 상태를 한 행으로 합쳐 GamePlay에 표시합니다.
public sealed class TeamVoiceMemberViewModel : INotifyPropertyChanged
{
    private string _displayName;
    private bool _isLocal;
    private bool _isSpeaking;
    private bool _isMuted;
    private bool _isVoiceConnected;
    private bool _isBusy;

    public TeamVoiceMemberViewModel(string identity, string displayName)
    {
        Identity = identity;
        _displayName = displayName;
    }

    public string Identity { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetField(ref _displayName, value);
    }

    public bool IsLocal
    {
        get => _isLocal;
        private set
        {
            if (SetField(ref _isLocal, value))
            {
                OnPropertyChanged(nameof(DisplayNameWithSelf));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(MuteGlyph));
            }
        }
    }

    public bool IsSpeaking
    {
        get => _isSpeaking;
        private set => SetField(ref _isSpeaking, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetField(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(MuteGlyph));
            }
        }
    }

    public bool IsVoiceConnected
    {
        get => _isVoiceConnected;
        private set
        {
            if (SetField(ref _isVoiceConnected, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(RowOpacity));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(RowOpacity));
            }
        }
    }

    public string DisplayNameWithSelf => IsLocal ? $"{DisplayName} (나)" : DisplayName;

    public string MuteGlyph => IsMuted
        ? "🔇"
        : IsLocal ? "🎙️" : "🔊";

    public string StatusText => IsBusy
        ? "설정 변경 중..."
        : !IsVoiceConnected
            ? "보이스 연결 대기"
            : IsLocal
                ? IsMuted ? "내 마이크 꺼짐" : "내 마이크 켜짐"
                : IsMuted ? "내 기기에서 음소거" : "듣는 중";

    public double RowOpacity => IsBusy ? 0.55 : IsVoiceConnected ? 1.0 : 0.72;

    public void Update(
        string displayName,
        bool isLocal,
        bool isSpeaking,
        bool isMuted,
        bool isVoiceConnected)
    {
        DisplayName = displayName;
        IsLocal = isLocal;
        IsSpeaking = isSpeaking;
        IsMuted = isMuted;
        IsVoiceConnected = isVoiceConnected;
        OnPropertyChanged(nameof(DisplayNameWithSelf));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
