using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.Tiled;
using MonoGame.Extended.Tiled.Renderers;
using MonoGame.Extended.ViewportAdapters;

namespace DemoGame;

public class Game1 : Game
{
    private const float CameraSpeed = 500.0f;
    private const float ZoomSpeed = 0.3f;

    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private SpriteFont _font;
    private OrthographicCamera _camera;
    private TiledMapRenderer _mapRenderer;
    private ViewportAdapter _viewportAdapter;
    private KeyboardState _previousKeyboardState;
    private bool _showHelp;
    private TiledMap _map;
    private Effect _customEffect;
    private Queue<string> _availableMaps;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        base.Initialize();
        _viewportAdapter = new BoxingViewportAdapter(Window, GraphicsDevice, 1024, 768);
        _camera = new OrthographicCamera(_viewportAdapter);
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _font = Content.Load<SpriteFont>("Fonts/Arial");

        // Just using level 5 and 8 from samples project
        _availableMaps = new Queue<string>(new[] { "level05", "level08" });

        _map = LoadNextMap();
        _camera.LookAt(new Vector2(_map.WidthInPixels, _map.HeightInPixels) * 0.5f);
        _camera.Position = new Vector2(-104, -92);

        _customEffect = new CustomEffect(GraphicsDevice)
        {
            Alpha = 0.5f,
            TextureEnabled = true,
            VertexColorEnabled = false
        };
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        var deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var keyboardState = Keyboard.GetState();

        _mapRenderer.Update(gameTime);

        if (keyboardState.IsKeyDown(Keys.Escape))
        {
            Exit();
        }

        var moveDirection = Vector2.Zero;

        if (keyboardState.IsKeyDown(Keys.W) || keyboardState.IsKeyDown(Keys.Up))
            moveDirection -= Vector2.UnitY;

        if (keyboardState.IsKeyDown(Keys.A) || keyboardState.IsKeyDown(Keys.Left))
            moveDirection -= Vector2.UnitX;

        if (keyboardState.IsKeyDown(Keys.S) || keyboardState.IsKeyDown(Keys.Down))
            moveDirection += Vector2.UnitY;

        if (keyboardState.IsKeyDown(Keys.D) || keyboardState.IsKeyDown(Keys.Right))
            moveDirection += Vector2.UnitX;

        // need to normalize the direction vector incase moving diagonally, but can't normalize the zero vector
        // however, the zero vector means we didn't want to move this frame anyways so all good
        var isCameraMoving = moveDirection != Vector2.Zero;
        if (isCameraMoving)
        {
            moveDirection.Normalize();
            _camera.Move(moveDirection * CameraSpeed * deltaSeconds);
        }

        if (keyboardState.IsKeyDown(Keys.R))
            _camera.ZoomIn(ZoomSpeed * deltaSeconds);

        if (keyboardState.IsKeyDown(Keys.F))
            _camera.ZoomOut(ZoomSpeed * deltaSeconds);

        if (_previousKeyboardState.IsKeyDown(Keys.Tab) && keyboardState.IsKeyUp(Keys.Tab))
        {
            _map = LoadNextMap();
            LookAtMapCenter();
        }

        if (_previousKeyboardState.IsKeyDown(Keys.H) && keyboardState.IsKeyUp(Keys.H))
            _showHelp = !_showHelp;

        if (keyboardState.IsKeyDown(Keys.Z))
            _camera.Position = Vector2.Zero;

        if (keyboardState.IsKeyDown(Keys.X))
            _camera.LookAt(Vector2.Zero);

        if (keyboardState.IsKeyDown(Keys.C))
            LookAtMapCenter();

        _previousKeyboardState = keyboardState;

    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;

        var viewMatrix = _camera.GetViewMatrix();
        var projectionMatrix = Matrix.CreateOrthographicOffCenter(0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, 0, 0f, -1f);

        _mapRenderer.Draw(ref viewMatrix, ref projectionMatrix, _customEffect);

        DrawText();

        base.Draw(gameTime);
    }

    private void DrawText()
    {
        var help = !_showHelp ? "H: Show Help" :
        $"""
        H: Hide help
        WASD/Arrows: Pan Camera
        RF: Zoom Camera In/Out
        Z: Move Camera To Origin
        X: Move Camera To Look At The Origin
        C: Move Camera To Look At Center Of The Map
        TAB: Cycle Through Maps
        """;

        var text =
        $"""
        Map: {_map.Name}; {_map.TileLayers.Count} tile layer(s) @ {_map.Width}x{_map.Height} tiles, {_map.ImageLayers.Count} image layer(s)
        Camera Position: (x={_camera.Position.X}, y={_camera.Position.Y})
        {help}
        """;

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        _spriteBatch.DrawString(_font, text, new Vector2(5, 0), Color.Black);
        _spriteBatch.End();
    }

    private TiledMap LoadNextMap()
    {
        var name = _availableMaps.Dequeue();
        _map = Content.Load<TiledMap>($"TiledMaps/{name}");
        _availableMaps.Enqueue(name);

        _mapRenderer?.Dispose();
        _mapRenderer = null;
        GC.Collect();

        _mapRenderer = new TiledMapRenderer(GraphicsDevice, _map);
        return _map;
    }

    private void LookAtMapCenter()
    {
        switch (_map.Orientation)
        {
            case TiledMapOrientation.Orthogonal:
                _camera.LookAt(new Vector2(_map.WidthInPixels, _map.HeightInPixels) * 0.5f);
                break;
            case TiledMapOrientation.Isometric:
                _camera.LookAt(new Vector2(0, _map.HeightInPixels + _map.TileHeight) * 0.5f);
                break;
            case TiledMapOrientation.Staggered:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
