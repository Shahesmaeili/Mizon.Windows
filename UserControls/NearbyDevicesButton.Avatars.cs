using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Symbol = FluentIcons.Common.Symbol;

namespace Mizon.Windows.UserControls;

public sealed partial class NearbyDevicesButton
{
    private const int AvatarLookupLimit = 32;
    private CancellationTokenSource? _avatarLifetime;
    private Task _avatarWorker = Task.CompletedTask;
    private readonly Queue<string> _avatarQueue = new();
    private readonly Dictionary<string, ImageSource?> _avatars = new(StringComparer.OrdinalIgnoreCase);

    private void Devices_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not NearbyDeviceRow { Account: { Length: >= 2 } account } row
            || _avatarLifetime is null || ViewModel is not { } viewModel) return;

        if (_avatars.TryGetValue(account, out var image))
        {
            row.Avatar = image;
            return;
        }
        if (_avatars.Count >= AvatarLookupLimit) return;

        _avatars.Add(account, null);
        _avatarQueue.Enqueue(account);
        if (_avatarWorker.IsCompleted)
            _avatarWorker = ReadVisibleAvatarsAsync(viewModel, _avatarLifetime.Token);
    }

    private async Task ReadVisibleAvatarsAsync(HomePageViewModel viewModel, CancellationToken lifetime)
    {
        while (!lifetime.IsCancellationRequested && _avatarQueue.TryDequeue(out var account))
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            request.CancelAfter(TimeSpan.FromSeconds(6));
            try
            {
                var image = await viewModel.ReadNearbyAvatarAsync(account, request.Token);
                request.Token.ThrowIfCancellationRequested();
                _avatars[account] = image;
                foreach (var row in _rows)
                    if (string.Equals(row.Account, account, StringComparison.OrdinalIgnoreCase)) row.Avatar = image;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception error)
            {
                AppLog.Information($"Nearby avatar was unavailable ({error.GetType().Name}).");
            }
        }
    }

    private void ReleaseAvatars()
    {
        _avatarLifetime?.Cancel();
        _avatarLifetime?.Dispose();
        _avatarLifetime = null;
        _avatarQueue.Clear();
        _avatars.Clear();
        _avatarWorker = Task.CompletedTask;
    }
}

public sealed class NearbyDeviceRow(Guid id, string? account, string name, Symbol symbol, string identity, string platformVersion,
    SolidColorBrush iconBrush, FontFamily font) : ViewModelBase
{
    internal Guid Id { get; } = id;
    internal string? Account { get; } = account;
    public string Name { get; } = name;
    public Symbol Symbol { get; } = symbol;
    public SolidColorBrush IconBrush { get; } = iconBrush;
    public Color Tint => IconBrush.Color;
    public string Identity { get; } = identity;
    public string PlatformVersion { get; } = platformVersion;
    public string AccessibleName => Identity.Length == 0 ? $"{Name}, {PlatformVersion}" : $"{Name}, {PlatformVersion}, {Identity}";
    public FontFamily Font { get; } = font;
    public ImageSource? Avatar
    {
        get => field;
        internal set
        {
            if (SetField(ref field, value)) OnPropertyChanged(nameof(AvatarVisibility));
        }
    }

    public Visibility AvatarVisibility => Avatar is null ? Visibility.Collapsed : Visibility.Visible;

    internal bool SamePresentation(NearbyDeviceRow other) => Name == other.Name && Symbol == other.Symbol
        && Identity == other.Identity && Account == other.Account && PlatformVersion == other.PlatformVersion
        && ReferenceEquals(IconBrush, other.IconBrush) && ReferenceEquals(Font, other.Font);
}
