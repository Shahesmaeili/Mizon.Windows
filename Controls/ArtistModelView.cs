using Microsoft.Graphics.DirectX;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.Scenes;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

/// <summary>A native, textured GLB model preview. Drag rotates the mesh, not a flat image.</summary>
public sealed partial class ArtistModelView : UserControl
{
    private const float DefaultYaw = .24f;
    private const float DefaultPitch = -.04f;
    private readonly Grid _surface = new();
    private readonly Grid _modelHost = new() { IsHitTestVisible = false };
    private readonly TextBlock _status = new()
    {
        Text = "بارگذاری مدل سه‌بعدی…", FontSize = 11, Opacity = .7,
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(16), IsHitTestVisible = false
    };
    private readonly UISettings _uiSettings = new();
    private CancellationTokenSource? _loadCancellation;
    private ModelScene? _scene;
    private bool _loaded;
    private bool _textureUpgradeQueued;
    private uint? _pointerId;
    private Vector2 _lastPointer;
    private Quaternion _dragStartOrientation = Quaternion.Identity;
    private Vector2 _dragRotation;
    private float _dragStartPitch;
    private int _heldRotationKeys;
    private readonly ArtistModelMotion _motion = new();
    private bool _motionSubscribed;
    private long _lastMotionTimestamp;
    private bool _animationsEnabled;
    private bool _isMouseOver;
    private bool _dragWithMouse;
    private InputCursor? _hoverCursor;
    private InputCursor? _dragCursor;

    public ArtistModelView()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Background = new SolidColorBrush(Colors.Transparent);
        _surface.Background = new SolidColorBrush(Colors.Transparent);
        _surface.Children.Add(_modelHost);
        _surface.Children.Add(_status);
        Content = _surface;
        AutomationProperties.SetName(this, "مدل سه‌بعدی هنرمند");
        AutomationProperties.SetHelpText(this, "برای چرخش مدل را بگیرید و بکشید؛ با رها کردن به حالت اولیه برمی‌گردد. کلیدهای جهت هم همین رفتار را دارند.");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCaptureLost;
        DoubleTapped += OnDoubleTapped;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        LostFocus += OnLostFocus;
    }

    public string ModelPath
    {
        get => (string)GetValue(ModelPathProperty);
        set => SetValue(ModelPathProperty, value);
    }

    public static readonly DependencyProperty ModelPathProperty = DependencyProperty.Register(
        nameof(ModelPath), typeof(string), typeof(ArtistModelView), new PropertyMetadata(string.Empty, OnModelPathChanged));

    /// <summary>The asset's front-facing angle, in degrees. Reset returns to this angle.</summary>
    public double InitialYawInDegrees
    {
        get => (double)GetValue(InitialYawInDegreesProperty);
        set => SetValue(InitialYawInDegreesProperty, value);
    }

    public static readonly DependencyProperty InitialYawInDegreesProperty = DependencyProperty.Register(
        nameof(InitialYawInDegrees), typeof(double), typeof(ArtistModelView),
        new PropertyMetadata(DefaultYaw * 180d / Math.PI, OnInitialYawChanged));

    private static void OnInitialYawChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ArtistModelView)sender).ResetView();

    /// <summary>Magnification relative to the model's automatic fit.</summary>
    public double ViewScale
    {
        get => (double)GetValue(ViewScaleProperty);
        set => SetValue(ViewScaleProperty, value);
    }

    public static readonly DependencyProperty ViewScaleProperty = DependencyProperty.Register(
        nameof(ViewScale), typeof(double), typeof(ArtistModelView), new PropertyMetadata(1d, OnViewportChanged));

    /// <summary>Vertical point to frame in normalized model coordinates (-1 to 1).</summary>
    public double VerticalFocus
    {
        get => (double)GetValue(VerticalFocusProperty);
        set => SetValue(VerticalFocusProperty, value);
    }

    public static readonly DependencyProperty VerticalFocusProperty = DependencyProperty.Register(
        nameof(VerticalFocus), typeof(double), typeof(ArtistModelView), new PropertyMetadata(0d, OnViewportChanged));

    private static void OnViewportChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (ArtistModelView)sender;
        control.UpdateViewport();
        control.QueueTextureUpgrade();
    }

    public void ResetView() => ResetView(animate: true);

    private void ResetView(bool animate)
    {
        _pointerId = null;
        _dragWithMouse = false;
        _heldRotationKeys = 0;
        ReleasePointerCaptures();
        UpdateViewport();
        ReturnHome(animate);
        UpdateCursor();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private static void OnModelPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (ArtistModelView)sender;
        if (control._loaded) control.LoadModel();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        _animationsEnabled = _uiSettings.AnimationsEnabled;
        _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        LoadModel();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_loaded) return;
        _loaded = false;
        _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        StopMotion();
        _isMouseOver = false;
        _pointerId = null;
        _dragWithMouse = false;
        _heldRotationKeys = 0;
        ReleasePointerCaptures();
        ProtectedCursor = null;
        _hoverCursor?.Dispose();
        _dragCursor?.Dispose();
        _hoverCursor = _dragCursor = null;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        ReleaseScene();
    }

    private async void LoadModel(bool preserveView = false)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var token = cancellation.Token;
        AppLog.Information($"[ArtistModelView] Loading '{ModelPath}'; BaseDirectory='{AppContext.BaseDirectory}'; View={ActualWidth:0.##}x{ActualHeight:0.##}; Host={_modelHost.ActualWidth:0.##}x{_modelHost.ActualHeight:0.##}; IsLoaded={IsLoaded}.");
        preserveView &= _scene is not null;
        if (!preserveView)
        {
            ReleaseScene();
            _status.Visibility = Visibility.Visible;
            _status.Text = "بارگذاری مدل سه‌بعدی…";
        }
        ModelScene? pending = null;
        var attached = false;
        try
        {
            if (string.IsNullOrWhiteSpace(ModelPath)) { _status.Text = "مدل سه‌بعدی انتخاب نشده"; return; }
            var model = await GlbModelLoader.LoadAsync(ModelPath, token);
            token.ThrowIfCancellationRequested();
            var compositor = ElementCompositionPreview.GetElementVisual(_modelHost).Compositor;
            pending = await CreateSceneAsync(compositor, model, GetTextureDecodeSize(), token);
            token.ThrowIfCancellationRequested();
            if (!_loaded) return;
            var previous = _scene;
            // A quality upgrade swaps only the rendering resources. Retain the
            // visible pose, pointer capture and spring while the new scene loads.
            ConfigureViewport(pending);
            if (preserveView)
            {
                pending.Root.Transform.Orientation = _motion.Orientation;
                pending.Presentation.Offset = _motion.Offset;
            }
            ElementCompositionPreview.SetElementChildVisual(_modelHost, pending.Presentation);
            _scene = pending;
            pending = null;
            previous?.Dispose();
            _status.Visibility = Visibility.Collapsed;
            if (!preserveView) ResetView(animate: false);
            UpdateCursor();
            attached = true;
            if (_animationsEnabled && !preserveView)
            {
                using var reveal = compositor.CreateScalarKeyFrameAnimation();
                reveal.InsertKeyFrame(0, 0);
                reveal.InsertKeyFrame(1, 1);
                reveal.Duration = TimeSpan.FromMilliseconds(240);
                _scene.Presentation.StartAnimation(nameof(Visual.Opacity), reveal);
            }
            AppLog.Information($"[ArtistModelView] Loaded {model.Meshes.Length} native meshes from '{ModelPath}'; View={ActualWidth:0.##}x{ActualHeight:0.##}; Host={_modelHost.ActualWidth:0.##}x{_modelHost.ActualHeight:0.##}; Presentation={_scene?.Presentation.Size}; IsLoaded={IsLoaded}; Scale={ViewScale:0.##}; Focus={VerticalFocus:0.##}.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
            {
                if (!preserveView)
                {
                    _status.Text = "نمایش مدل سه‌بعدی در دسترس نیست";
                    _status.Visibility = Visibility.Visible;
                }
                AppLog.Error($"[ArtistModelView] Failed loading '{ModelPath}'; BaseDirectory='{AppContext.BaseDirectory}'; View={ActualWidth:0.##}x{ActualHeight:0.##}; Host={_modelHost.ActualWidth:0.##}x{_modelHost.ActualHeight:0.##}; Presentation={_scene?.Presentation.Size}; IsLoaded={IsLoaded}.", exception);
            }
        }
        finally
        {
            pending?.Dispose();
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                cancellation.Dispose();
                if (attached) QueueTextureUpgrade();
            }
        }
    }

    private double GetTextureDecodeSize()
    {
        // LoadedImageSurface accepts DIPs and applies the display's DPI itself.
        // Four texels per displayed DIP leave headroom for the face, crop and
        // rotation; the 1024-DIP floor also covers the compact/wide layouts.
        // Buckets avoid reallocating GPU surfaces for each resize pixel.
        var viewport = Math.Min(ActualWidth, ActualHeight);
        if (!double.IsFinite(viewport) || viewport <= 0) viewport = 256;
        var scale = double.IsFinite(ViewScale) ? Math.Clamp(ViewScale, .5, 2.5) : 1;
        return Math.Clamp(Math.Ceiling(viewport * scale * 4 / 512) * 512, 1024, 2048);
    }

    private void QueueTextureUpgrade()
    {
        if (!_loaded || _scene is not { HasReducedTextures: true } || _loadCancellation is not null || _textureUpgradeQueued
            || GetTextureDecodeSize() <= _scene.TextureDecodeSize) return;
        _textureUpgradeQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _textureUpgradeQueued = false;
            if (_loaded && _scene is { HasReducedTextures: true } && _loadCancellation is null
                && GetTextureDecodeSize() > _scene.TextureDecodeSize)
                LoadModel(preserveView: true);
        });
    }

    private static async Task<ModelScene> CreateSceneAsync(Compositor compositor, GlbModelData model,
        double textureDecodeSize, CancellationToken cancellationToken)
    {
        var resources = new List<IDisposable>();
        // The loader shares each image's byte array across all primitives using it.
        // Reuse the decoded GPU surface as well; large character meshes often have
        // dozens of parts but only a few actual textures.
        var textureInputs = new Dictionary<byte[], SceneSurfaceMaterialInput>();
        var hasReducedTextures = false;
        T Own<T>(T item) where T : IDisposable { resources.Add(item); return item; }
        try
        {
            var visual = Own(SceneVisual.Create(compositor));
            // Keep the portrait's clip in viewport coordinates, separate from
            // the model's scale and orientation.
            var presentation = Own(compositor.CreateLayerVisual());
            presentation.Children.InsertAtTop(visual);
            presentation.Clip = Own(compositor.CreateInsetClip());
            var root = Own(SceneNode.Create(compositor));
            visual.Root = root;
            foreach (var data in model.Meshes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mesh = Own(SceneMesh.Create(compositor));
                mesh.PrimitiveTopology = DirectXPrimitiveTopology.TriangleList;
                Fill(mesh, SceneAttributeSemantic.Vertex, DirectXPixelFormat.R32G32B32Float, MemoryMarshal.AsBytes(data.Positions.AsSpan()));
                Fill(mesh, SceneAttributeSemantic.Normal, DirectXPixelFormat.R32G32B32Float, MemoryMarshal.AsBytes(data.Normals.AsSpan()));
                Fill(mesh, SceneAttributeSemantic.TexCoord0, DirectXPixelFormat.R32G32Float, MemoryMarshal.AsBytes(data.TextureCoordinates.AsSpan()));
                Fill(mesh, SceneAttributeSemantic.Index, DirectXPixelFormat.R32UInt, MemoryMarshal.AsBytes(data.Indices.AsSpan()));
                var material = Own(SceneMetallicRoughnessMaterial.Create(compositor));
                material.BaseColorFactor = data.BaseColor;
                material.MetallicFactor = data.Metallic;
                material.RoughnessFactor = Math.Max(.1f, data.Roughness);
                material.IsDoubleSided = true;
                material.AlphaMode = data.AlphaMode switch
                {
                    "BLEND" => SceneAlphaMode.Blend,
                    "MASK" => SceneAlphaMode.AlphaTest,
                    _ => SceneAlphaMode.Opaque
                };
                material.AlphaCutoff = data.AlphaCutoff;
                var renderer = Own(SceneMeshRendererComponent.Create(compositor));
                renderer.Mesh = mesh;
                renderer.Material = material;
                if (data.TextureBytes is { Length: > 0 } texture)
                {
                    if (!textureInputs.TryGetValue(texture, out var input))
                    {
                        var isJpeg = texture.Length >= 3 && texture[0] == 0xff && texture[1] == 0xd8 && texture[2] == 0xff;
                        var isPng = texture.Length >= 8 && MemoryExtensions.SequenceEqual<byte>(texture.AsSpan(0, 8), [137, 80, 78, 71, 13, 10, 26, 10]);
                        if (!isJpeg && !isPng) throw new InvalidDataException("A GLB preview texture must be an embedded JPEG or PNG.");
                        // Share the cached compressed bytes instead of retaining
                        // a second native copy. Keep both adapters alive until
                        // the surface is disposed; it can read its source again.
                        var bytes = Own(new MemoryStream(texture, writable: false));
                        var stream = Own(bytes.AsRandomAccessStream());
                        var surface = Own(LoadedImageSurface.StartLoadFromStream(stream,
                            new Size(textureDecodeSize, textureDecodeSize)));
                        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        void OnImageLoaded(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args)
                        {
                            if (args.Status == LoadedImageSourceLoadStatus.Success) loaded.TrySetResult();
                            else loaded.TrySetException(new InvalidDataException($"Model texture loading failed: {args.Status}."));
                        }
                        surface.LoadCompleted += OnImageLoaded;
                        try { await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); }
                        finally { surface.LoadCompleted -= OnImageLoaded; }
                        // Scene materials accept a mipmap or visual surface, not a
                        // LoadedImageSurface directly. Capture an offscreen textured sprite.
                        var textureSize = new Vector2((float)surface.DecodedPhysicalSize.Width, (float)surface.DecodedPhysicalSize.Height);
                        if (textureSize.X <= 0 || textureSize.Y <= 0) throw new InvalidDataException("Empty model texture.");
                        hasReducedTextures |= textureSize.X < surface.NaturalSize.Width || textureSize.Y < surface.NaturalSize.Height;
                        AppLog.Information($"[ArtistModelView] Texture {surface.NaturalSize.Width:0}x{surface.NaturalSize.Height:0} -> {textureSize.X:0}x{textureSize.Y:0} pixels; decode limit={textureDecodeSize:0} DIPs.");
                        var brush = Own(compositor.CreateSurfaceBrush(surface));
                        brush.Stretch = CompositionStretch.Fill;
                        var textureVisual = Own(compositor.CreateSpriteVisual());
                        textureVisual.Size = textureSize;
                        textureVisual.Brush = brush;
                        var textureSurface = Own(compositor.CreateVisualSurface());
                        textureSurface.SourceVisual = textureVisual;
                        textureSurface.SourceSize = textureSize;
                        input = Own(SceneSurfaceMaterialInput.Create(compositor));
                        input.Surface = textureSurface;
                        input.BitmapInterpolationMode = CompositionBitmapInterpolationMode.MagLinearMinLinearMipLinear;
                        input.WrappingUMode = SceneWrappingMode.Repeat;
                        input.WrappingVMode = SceneWrappingMode.Repeat;
                        textureInputs.Add(texture, input);
                    }
                    material.BaseColorInput = input;
                    renderer.UVMappings.Add("BaseColorInput", SceneAttributeSemantic.TexCoord0);
                }
                var node = Own(SceneNode.Create(compositor));
                node.Components.Add(renderer);
                root.Children.Add(node);
            }
            // These are retained with the scene and never recreated while dragging.
            var ambient = Own(compositor.CreateAmbientLight());
            ambient.Color = Colors.White;
            ambient.Intensity = .8f;
            ambient.Targets.Add(visual);
            var key = Own(compositor.CreateDistantLight());
            key.Color = Colors.White;
            key.Intensity = .7f;
            key.Direction = Vector3.Normalize(new Vector3(-.5f, -.7f, -1));
            key.CoordinateSpace = visual;
            key.Targets.Add(visual);
            return new ModelScene(visual, presentation, root, textureDecodeSize, hasReducedTextures, resources);
        }
        catch
        {
            DisposeResources(resources);
            throw;
        }
    }

    private static void DisposeResources(List<IDisposable> resources)
    {
        // Scenes retain native references between their nodes, meshes and materials.
        // Break the graph before closing its objects (including failed partial loads).
        foreach (var item in resources)
        {
            if (item is LayerVisual layer)
            {
                layer.Children.RemoveAll();
                layer.Clip = null;
            }
            if (item is CompositionLight light) light.Targets.RemoveAll();
            if (item is DistantLight distant) distant.CoordinateSpace = null;
            if (item is SceneVisual visual) visual.Root = null;
            if (item is SceneNode node)
            {
                node.Children.Clear();
                node.Components.Clear();
            }
            if (item is SceneMeshRendererComponent renderer)
            {
                renderer.Mesh = null;
                renderer.Material = null;
            }
            if (item is SceneMetallicRoughnessMaterial material) material.BaseColorInput = null;
            if (item is SceneSurfaceMaterialInput input) input.Surface = null;
            if (item is CompositionVisualSurface textureSurface) textureSurface.SourceVisual = null;
            if (item is SpriteVisual textureVisual) textureVisual.Brush = null;
            if (item is CompositionSurfaceBrush brush) brush.Surface = null;
        }
        for (var i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
        resources.Clear();
    }

    private static unsafe void Fill(SceneMesh mesh, SceneAttributeSemantic semantic, DirectXPixelFormat format, ReadOnlySpan<byte> data)
    {
        using var buffer = new MemoryBuffer(checked((uint)data.Length));
        using var reference = buffer.CreateReference();
        // Query the native buffer directly. No runtime-generated COM interop or reflection
        // is required, so this path also works when the app is published with NativeAOT.
        var native = ((WinRT.IWinRTObject)reference).NativeObject;
        var iid = new Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native.ThisPtr, in iid, out var access));
        try
        {
            byte* destination;
            uint capacity;
            var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, int>)(*(nint**)access)[3];
            Marshal.ThrowExceptionForHR(getBuffer(access, &destination, &capacity));
            if (capacity < data.Length || destination is null) throw new InvalidDataException("Insufficient native mesh buffer.");
            fixed (byte* source = data) System.Buffer.MemoryCopy(source, destination, capacity, data.Length);
            mesh.FillMeshAttribute(semantic, format, buffer);
        }
        finally { Marshal.Release(access); }
    }

    private void ReleaseScene()
    {
        StopMotion();
        _pointerId = null;
        _dragWithMouse = false;
        _heldRotationKeys = 0;
        ReleasePointerCaptures();
        ElementCompositionPreview.SetElementChildVisual(_modelHost, null);
        _scene?.Dispose();
        _scene = null;
        UpdateCursor();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateViewport();
        QueueTextureUpgrade();
    }

    private void UpdateViewport()
    {
        if (_scene is not null) ConfigureViewport(_scene);
    }

    private void ConfigureViewport(ModelScene scene)
    {
        if (ActualWidth < 1 || ActualHeight < 1) return;
        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        scene.Presentation.Size = new Vector2(width, height);
        scene.Visual.Size = new Vector2(width, height);
        scene.Visual.TransformMatrix = CreateViewTransform(width, height, ViewScale, VerticalFocus);
    }

    internal static Matrix4x4 CreateViewTransform(float width, float height, double viewScale = 1, double verticalFocus = 0)
    {
        var scale = double.IsFinite(viewScale) ? (float)Math.Clamp(viewScale, .5, 2.5) : 1;
        var focus = double.IsFinite(verticalFocus) ? (float)Math.Clamp(verticalFocus, -1, 1) : 0;
        var pixelsPerUnit = Math.Min(width, height) * .45f * scale;
        // SceneVisual needs a transform in Composition viewport units. A Direct3D
        // clip-space perspective here collapses the rendered meshes to subpixels.
        // Preserve depth in the same units as X/Y and let the scene render its
        // rotating 3D geometry. Its native Y convention already keeps glTF upright.
        return Matrix4x4.CreateScale(pixelsPerUnit) * Matrix4x4.CreateTranslation(
            width * .5f, height * .5f + focus * pixelsPerUnit, 0);
    }

    private void ReturnHome(bool animate = true)
    {
        if (_scene is null) return;
        var yaw = double.IsFinite(InitialYawInDegrees)
            ? (float)(Math.IEEERemainder(InitialYawInDegrees, 360) * Math.PI / 180) : DefaultYaw;
        _motion.ReturnTo(Quaternion.CreateFromYawPitchRoll(yaw, DefaultPitch, 0));
        ScheduleMotion(animate);
    }

    private void BeginRotation()
    {
        // Regrabbing during the return starts at the visible pose, including any
        // interpolated roll. Rebuilding Euler angles here would cause a jump.
        _motion.BeginDrag();
        _dragStartOrientation = _motion.Orientation;
        _dragStartPitch = MathF.Asin(Math.Clamp(2 * (_dragStartOrientation.W * _dragStartOrientation.X
            - _dragStartOrientation.Y * _dragStartOrientation.Z), -1, 1));
        _dragRotation = Vector2.Zero;
        StopMotion();
    }

    private void RotateBy(Vector2 delta)
    {
        _dragRotation += delta;
        // Increasing resistance makes the pose feel attached to its resting point.
        // The derivative is one at the origin, so small drags stay responsive.
        var yaw = MathF.PI * MathF.Tanh(_dragRotation.X / MathF.PI);
        var pitch = Math.Clamp(1.2f * MathF.Tanh(_dragRotation.Y / 1.2f),
            -1.2f - _dragStartPitch, 1.2f - _dragStartPitch);
        var target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * _dragStartOrientation
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
        _motion.SetTarget(target, Vector3.Zero);
        ScheduleMotion();
    }

    private void ScheduleMotion(bool animate = true)
    {
        if (_scene is null) return;
        if (!animate || !_animationsEnabled)
        {
            StopMotion();
            _motion.SnapToTarget();
            PresentMotion();
            return;
        }
        // High-polling mice may deliver many events between frames. Keep only the
        // latest target and update the retained scene once per rendered frame.
        if (!_motionSubscribed)
        {
            _motionSubscribed = true;
            _lastMotionTimestamp = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnMotionFrame;
        }
    }

    private void OnMotionFrame(object? sender, object args)
    {
        if (!_loaded || _scene is null)
        {
            StopMotion();
            return;
        }
        var now = Stopwatch.GetTimestamp();
        var elapsed = (float)Stopwatch.GetElapsedTime(_lastMotionTimestamp, now).TotalSeconds;
        _lastMotionTimestamp = now;
        _motion.Advance(elapsed, dragging: _pointerId is not null || _heldRotationKeys != 0);
        PresentMotion();
        if (_motion.IsSettled) StopMotion();
    }

    private void PresentMotion()
    {
        if (_scene is null) return;
        _scene.Root.Transform.Orientation = _motion.Orientation;
        _scene.Presentation.Offset = _motion.Offset;
    }

    private void StopMotion()
    {
        if (!_motionSubscribed) return;
        CompositionTarget.Rendering -= OnMotionFrame;
        _motionSubscribed = false;
        _lastMotionTimestamp = 0;
    }

    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loaded) return;
            _animationsEnabled = _uiSettings.AnimationsEnabled;
            if (!_animationsEnabled) ScheduleMotion(animate: false);
        });
    }

    private void UpdateCursor()
    {
        if (!_loaded || _scene is null || (!_isMouseOver && !_dragWithMouse))
            ProtectedCursor = null;
        else if (_dragWithMouse)
            ProtectedCursor = _dragCursor ??= GrabCursorFactory.Create(grabbing: true, XamlRoot?.RasterizationScale ?? 1);
        else
            ProtectedCursor = _hoverCursor ??= GrabCursorFactory.Create(grabbing: false, XamlRoot?.RasterizationScale ?? 1);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        _isMouseOver = true;
        UpdateCursor();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        _isMouseOver = false;
        UpdateCursor();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_scene is null || _pointerId is not null) return;
        var point = args.GetCurrentPoint(this);
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed) return;
        if (!CapturePointer(args.Pointer)) return;
        Focus(FocusState.Pointer);
        _pointerId = args.Pointer.PointerId;
        _dragWithMouse = args.Pointer.PointerDeviceType == PointerDeviceType.Mouse;
        _heldRotationKeys = 0;
        BeginRotation();
        UpdateCursor();
        _lastPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        args.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_pointerId is null) return;
        if (_pointerId != args.Pointer.PointerId) return;
        var point = args.GetCurrentPoint(this).Position;
        var position = new Vector2((float)point.X, (float)point.Y);
        var delta = position - _lastPointer;
        _lastPointer = position;
        RotateBy(new Vector2(delta.X * .012f, -delta.Y * .009f));
        args.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_pointerId != args.Pointer.PointerId) return;
        _pointerId = null;
        _dragWithMouse = false;
        ReleasePointerCapture(args.Pointer);
        UpdateCursor();
        ReturnHome();
        args.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (_pointerId != args.Pointer.PointerId) return;
        _isMouseOver = false;
        OnPointerReleased(sender, args);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (_pointerId != args.Pointer.PointerId) return;
        _pointerId = null;
        _dragWithMouse = false;
        UpdateCursor();
        ReturnHome();
    }
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs args) { ResetView(); args.Handled = true; }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Home) { ResetView(); args.Handled = true; return; }
        var key = RotationKeyMask(args.Key);
        if (key == 0 || _pointerId is not null || _scene is null) return;
        if (_heldRotationKeys == 0) BeginRotation();
        _heldRotationKeys |= key;
        RotateBy(args.Key switch
        {
            VirtualKey.Left => new Vector2(-.15f, 0),
            VirtualKey.Right => new Vector2(.15f, 0),
            VirtualKey.Up => new Vector2(0, .1f),
            _ => new Vector2(0, -.1f)
        });
        args.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs args)
    {
        var key = RotationKeyMask(args.Key);
        if ((_heldRotationKeys & key) == 0) return;
        _heldRotationKeys &= ~key;
        if (_heldRotationKeys == 0) ReturnHome();
        args.Handled = true;
    }

    private void OnLostFocus(object sender, RoutedEventArgs args)
    {
        if (_pointerId is not null) return;
        if (_heldRotationKeys == 0) return;
        _heldRotationKeys = 0;
        ReturnHome();
    }

    private static int RotationKeyMask(VirtualKey key) => key switch
    {
        VirtualKey.Left => 1,
        VirtualKey.Right => 2,
        VirtualKey.Up => 4,
        VirtualKey.Down => 8,
        _ => 0
    };

    private sealed class ModelScene(SceneVisual visual, LayerVisual presentation, SceneNode root, double textureDecodeSize, bool hasReducedTextures,
        List<IDisposable> resources) : IDisposable
    {
        private bool _disposed;
        public SceneVisual Visual { get; } = visual;
        public LayerVisual Presentation { get; } = presentation;
        public SceneNode Root { get; } = root;
        public double TextureDecodeSize { get; } = textureDecodeSize;
        public bool HasReducedTextures { get; } = hasReducedTextures;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Presentation.StopAnimation(nameof(Visual.Opacity));
            Presentation.StopAnimation(nameof(Visual.Offset));
            Root.Transform.StopAnimation(nameof(SceneModelTransform.Orientation));
            DisposeResources(resources);
        }
    }
}

