using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Fereastra aplicației și bucla principală (main game loop).
/// Față de laboratorul 3: o scenă cu mai multe obiecte, fiecare cu matricea lui model,
/// o cameră cu matricele view și projection, tastatura și mouse-ul.
/// </summary>
public class Game : GameWindow
{
    // Obiecte care NU depind de OpenGL pot fi create imediat.
    private readonly Camera _camera = new();

    // Obiectele care folosesc OpenGL se creează în OnLoad (abia atunci există contextul OpenGL).
    private Shader _shader = null!;
    private readonly List<SceneObject> _scene = new();
    private readonly List<Transform2D> _initialTransforms = new();   // pentru tasta R

    // Starea aplicației (se modifică în OnUpdateFrame, se folosește în OnRenderFrame).
    private int _selected;            // indexul obiectului selectat în _scene (tasta Tab)
    private bool _wireframe;          // contur (true) sau plin (false)
                                      // Variabile pentru T4 și T5
    private Vector2? _pivot = null;         // Punctul fix pentru rotație
    private Mesh _satelliteMesh = null!;    // Mesh-ul satelitului
    private float _satAngle = 0.0f;         // Unghiul curent al satelitului


    // Contoare afișate în titlul ferestrei.
    private readonly string _baseTitle;
    private double _secondTimer;
    private int _frames, _lastFrames;
    private bool _titleDirty = true;

    public Game(int width, int height, string title)
        : base(GameWindowSettings.Default,
               new NativeWindowSettings
               {
                   ClientSize = new Vector2i(width, height),
                   Title = title,
                   APIVersion = new Version(3, 3),          // cerem OpenGL 3.3 ...
                   Profile = ContextProfile.Core,           // ... profil Core (fără modul imediat)
                   Flags = ContextFlags.ForwardCompatible
               })
    {
        _baseTitle = title;
        VSync = VSyncMode.On;   // swap-ul bufferelor așteaptă reîmprospătarea monitorului
    }

    /// <summary>Apelată O SINGURĂ DATĂ, după crearea ferestrei și a contextului OpenGL.</summary>
    protected override void OnLoad()
    {
        base.OnLoad();

        Console.WriteLine($"OpenGL  : {GL.GetString(StringName.Version)}");
        Console.WriteLine($"Renderer: {GL.GetString(StringName.Renderer)}");

        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        _camera.Resize(ClientSize.X, ClientSize.Y);

        _shader = Shader.FromFiles("Shaders/shader.vert", "Shaders/shader.frag");

        // Scena: aceleași forme din laboratorul 3, acum plasate în lume prin Transform2D.
        // Depth (z) contează doar în perspectivă (tasta O, secțiunea 7.2).
        AddObject("pătrat", Shapes.Quad(),
                  new Transform2D { Position = new Vector2(-1.8f, -1.0f), Depth = -3.0f });
        AddObject("triunghi", Shapes.Triangle(),
                  new Transform2D { Position = new Vector2(-1.5f, 0), Rotation = 45, Scale = new Vector2(1.5f, 0.5f) });

        AddObject("hexagon", Shapes.RegularPolygon(6, 0.5f),
                  new Transform2D { Position = new Vector2(2.0f, 1.0f) });
        AddObject("cerc", Shapes.RegularPolygon(64, 0.5f),
                  new Transform2D { Position = new Vector2(0.8f, -0.6f), Depth = 2.0f });
        _satelliteMesh = Shapes.Triangle(); // Satelitul va fi un triunghi
    }

    /// <summary>Adaugă un obiect în scenă și reține transformarea lui inițială (pentru tasta R).</summary>
    private void AddObject(string name, Mesh mesh, Transform2D transform)
    {
        _scene.Add(new SceneObject(name, mesh, transform));
        _initialTransforms.Add(transform.Clone());
    }

    /// <summary>Apelată când se schimbă dimensiunea ferestrei.</summary>
    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);   // zona ferestrei în care se desenează
        _camera.Resize(e.Width, e.Height);      // proiecția se adaptează la noul raport de aspect
    }

    /// <summary>Apelată la fiecare iterație a buclei: LOGICA aplicației (intrări, mișcare, timp).</summary>
    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (KeyboardState.IsKeyDown(Keys.Escape))
        {
            Close();
        }

        // --- Obiectul selectat: tastatura (secțiunea 8) ------------------------------

                float dt = (float)args.Time;
        SceneObject sel = _scene[_selected];

        // Multiplicator pentru tasta Shift (de 3 ori mai rapid dacă e apăsat)
        float mult = KeyboardState.IsKeyDown(Keys.LeftShift) ? 3.0f : 1.0f;
        float modDt = dt * mult; // Folosim acest "timp modificat" la toate mișcările

        const float moveSpeed = 2.0f;   // unități de lume pe secundă
        if (KeyboardState.IsKeyDown(Keys.Left))  sel.Transform.Position.X -= moveSpeed * modDt;
        if (KeyboardState.IsKeyDown(Keys.Right)) sel.Transform.Position.X += moveSpeed * modDt;
        if (KeyboardState.IsKeyDown(Keys.Down))  sel.Transform.Position.Y -= moveSpeed * modDt;
        if (KeyboardState.IsKeyDown(Keys.Up))    sel.Transform.Position.Y += moveSpeed * modDt;

        // T2: Q / E rotesc obiectul, Z / X îl scalează
        const float rotSpeed = 90.0f; // grade pe secundă
        if (KeyboardState.IsKeyDown(Keys.Q)) sel.Transform.Rotation += rotSpeed * modDt; // Sens trigonometric
        if (KeyboardState.IsKeyDown(Keys.E)) sel.Transform.Rotation -= rotSpeed * modDt; // Sens orar

        const float scaleFactor = 1.5f; // factor pe secundă
        if (KeyboardState.IsKeyDown(Keys.X)) sel.Transform.Scale *= MathF.Pow(scaleFactor, modDt);
        if (KeyboardState.IsKeyDown(Keys.Z)) sel.Transform.Scale *= MathF.Pow(1.0f / scaleFactor, modDt);
        
        // Limitare scară între 0.1 și 10 pe ambele axe
        sel.Transform.Scale.X = Math.Clamp(sel.Transform.Scale.X, 0.1f, 10.0f);
        sel.Transform.Scale.Y = Math.Clamp(sel.Transform.Scale.Y, 0.1f, 10.0f);

        // T2 (Comentariu cerut): Scalarea se face prin înmulțire (și nu prin adunare) 
        // pentru ca mărirea și micșorarea să fie simetrice și proporționale. 
        // Dacă am scădea o valoare fixă, scara ar putea ajunge la 0 (obiect invizibil) 
        // sau la valori negative (ceea ce ar oglindi forma pe ecran). 
        // Prin înmulțire, dimensiunea se apropie asimptotic de 0 fără să-l atingă.


        // TODO (T2): Q / E rotesc obiectul, Z / X îl scalează

        if (KeyboardState.IsKeyPressed(Keys.Tab)) _selected = (_selected + 1) % _scene.Count;
        if (KeyboardState.IsKeyPressed(Keys.O))   _camera.IsPerspective = !_camera.IsPerspective;
        if (KeyboardState.IsKeyPressed(Keys.F))   _wireframe = !_wireframe;
        if (KeyboardState.IsKeyPressed(Keys.R))   ResetScene();

        if (KeyboardState.IsKeyPressed(Keys.Tab) || KeyboardState.IsKeyPressed(Keys.O) ||
            KeyboardState.IsKeyPressed(Keys.R))
        {
            _titleDirty = true;
        }

        // --- Obiectul selectat: mouse-ul (secțiunea 9) -------------------------------

        // T3: la clic stânga (IsButtonPressed), mutați obiectul selectat în punctul din lume aflat sub cursor.
        if (MouseState.IsButtonPressed(MouseButton.Left))
        {
            Vector2 worldPos = ScreenToWorld(MouseState.Position);
            sel.Transform.Position = worldPos;
            // Depth rămâne neschimbat.
        }


        // T4: Clic dreapta stabilește un pivot în punctul din lume aflat sub cursor
        if (MouseState.IsButtonPressed(MouseButton.Right))
        {
            _pivot = ScreenToWorld(MouseState.Position);
        }

        // T4: Rotație în jurul unui pivot când ținem apăsat P
        if (KeyboardState.IsKeyDown(Keys.P) && _pivot.HasValue)
        {
            float angleDeg = 90.0f * modDt; // 90 grade / secundă
            float angleRad = MathHelper.DegreesToRadians(angleDeg);

            // Matricea de rotație în jurul pivotului (T(-pivot) * R * T(pivot))
            Matrix4 pivotRot = Matrix4.CreateTranslation(-_pivot.Value.X, -_pivot.Value.Y, 0) *
                               Matrix4.CreateRotationZ(angleRad) *
                               Matrix4.CreateTranslation(_pivot.Value.X, _pivot.Value.Y, 0);

            // Aplicăm matricea pe poziția curentă a obiectului ca să îi aflăm noua coordonată
            Vector4 currentPos = new Vector4(sel.Transform.Position.X, sel.Transform.Position.Y, 0, 1);
            Vector4 newPos = currentPos * pivotRot;

            sel.Transform.Position = new Vector2(newPos.X, newPos.Y);
            sel.Transform.Rotation += angleDeg; // Îi creștem și unghiul propriu ca să "privească" spre pivot
        }

        // T5: Actualizăm unghiul satelitului. 
        // O rotație completă (2 * PI) în 4 secunde înseamnă PI / 2 radiani pe secundă.
        _satAngle += (MathF.PI / 2.0f) * dt;


        // --- Camera: tastatura și rotița (secțiunea 6.2) -----------------------------

        float camSpeed = 3.0f / _camera.Zoom;                    // unități de lume pe secundă
        if (KeyboardState.IsKeyDown(Keys.A)) _camera.Position.X -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.D)) _camera.Position.X += camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.S)) _camera.Position.Y -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.W)) _camera.Position.Y += camSpeed * dt;

        _camera.Zoom *= MathF.Pow(1.1f, MouseState.ScrollDelta.Y);   // un pas al rotiței = ±10 %
        _camera.Zoom = Math.Clamp(_camera.Zoom, 0.1f, 10.0f);

        // --- Titlul ferestrei: obiectul selectat, proiecția, cadre pe secundă --------

        _secondTimer += args.Time;
        if (_secondTimer >= 1.0)
        {
            _lastFrames = _frames;
            _frames = 0;
            _secondTimer -= 1.0;
            _titleDirty = true;
        }

        if (_titleDirty)
        {
            string projection = _camera.IsPerspective ? "perspectivă" : "ortografică";
            Title = $"{_baseTitle} | selectat: {_scene[_selected].Name} | proiecție {projection} | FPS: {_lastFrames}";
            _titleDirty = false;
        }
    }

    /// <summary>Apelată la fiecare iterație a buclei: DESENAREA unui cadru.</summary>
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        _frames++;

        // 1. Ștergem imaginea veche din back buffer.
        GL.ClearColor(0.12f, 0.14f, 0.18f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        // 2. Starea de rasterizare și programul shader.
        SetWireframe(_wireframe);
        _shader.Use();

        // 3. View și projection sunt aceleași pentru toate obiectele: le trimitem o singură dată.
        _shader.SetMatrix4("uView", _camera.ViewMatrix);
        _shader.SetMatrix4("uProjection", _camera.ProjectionMatrix);

        // 4. Fiecare obiect: matricea lui model, nuanța, apoi desenarea.
        foreach (SceneObject obj in _scene)
        {
            _shader.SetMatrix4("uModel", obj.Transform.ModelMatrix);
            _shader.SetColor("uTint", obj.Tint);
            obj.Mesh.Draw();
        }

        // T5: Desenarea satelitului în jurul obiectului selectat
        SceneObject selectedObj = _scene[_selected];

        // Matricea locală a satelitului: Scalare (0.3) -> Translație (1.2 pe X) -> Rotație în jurul propriei origini (care e acum centrul părintelui)
        Matrix4 localSat = Matrix4.CreateScale(0.3f, 0.3f, 1.0f) *
                           Matrix4.CreateTranslation(1.2f, 0.0f, 0.0f) *
                           Matrix4.CreateRotationZ(_satAngle);

        // Aici e secretul: Înmulțim transformarea locală a satelitului cu matricea model a părintelui său
        Matrix4 satModel = localSat * selectedObj.Transform.ModelMatrix;

        _shader.SetMatrix4("uModel", satModel);
        _shader.SetColor("uTint", Color4.DarkGray); // Îl facem gri ca să îl deosebim
        _satelliteMesh.Draw();

        // T5 (Comentariu cerut): Ce se întâmplă cu orbita când obiectul selectat este scalat, și de ce?
        // Când obiectul părinte este scalat neuniform (ex: alungit pe X), orbita satelitului se deformează 
        // și devine o elipsă. De asemenea, satelitul însuși se turtește/alungește.
        // Acest lucru se întâmplă deoarece transformarea părintelui se propagă direct asupra copilului:
        // matricea model a părintelui acționează asupra întregului "sistem de coordonate" al satelitului, 
        // deformând atât spațiul (orbita) cât și forma geometrică (mesh-ul) a acestuia.


        // 5. Afișăm cadrul terminat: back buffer <-> front buffer.
        SwapBuffers();
    }

    /// <summary>Apelată O SINGURĂ DATĂ, la închiderea ferestrei: eliberăm resursele GPU.</summary>
    protected override void OnUnload()
    {
        foreach (SceneObject obj in _scene)
        {
            obj.Dispose();
        }
        _shader.Dispose();

        base.OnUnload();
    }

    /// <summary>Transformă poziția mouse-ului (pixeli, origine stânga-sus) în coordonate de lume.</summary>
    private Vector2 ScreenToWorld(Vector2 mousePixels)
    {
        // T3: pasul 1 (pixel -> NDC)
        // Y-ul este inversat deoarece mouse-ul are y=0 sus, iar OpenGL are y=0 jos
        float x_ndc = 2.0f * mousePixels.X / ClientSize.X - 1.0f;
        float y_ndc = 1.0f - 2.0f * mousePixels.Y / ClientSize.Y;

        // T3 (Comentariu cerut): Semnul minus (sau formula 1 - 2*y/H) apare deoarece 
        // sistemul de operare și mouse-ul măsoară coordonata Y de sus în jos 
        // (originea e în stânga-sus), în timp ce spațiul NDC (și OpenGL în general) 
        // crește de jos în sus. Scăderea transformă și aliniază corect axele.
        Vector4 ndcPoint = new Vector4(x_ndc, y_ndc, 0.0f, 1.0f);

        // T3: pasul 2 (NDC -> lume)
        // Înmulțim cu inversa produsului ViewMatrix * ProjectionMatrix
        Matrix4 viewProj = _camera.ViewMatrix * _camera.ProjectionMatrix;
        Matrix4 inverseViewProj = Matrix4.Invert(viewProj);

        Vector4 worldPoint = ndcPoint * inverseViewProj;

        return new Vector2(worldPoint.X, worldPoint.Y);
    }


    /// <summary>Tasta R: obiectele revin la transformările inițiale, camera la starea inițială.</summary>
    private void ResetScene()
    {
        for (int i = 0; i < _scene.Count; i++)
        {
            _scene[i].Transform = _initialTransforms[i].Clone();
        }
        _camera.Reset();
    }

    /// <summary>
    /// Contur / plin. Dacă Visual Studio semnalează o eroare la primul parametru,
    /// folosiți aici tipul acceptat de versiunea voastră (vezi laboratorul 2, Problema 3):
    /// TriangleFace.FrontAndBack sau MaterialFace.FrontAndBack.
    /// </summary>
    private static void SetWireframe(bool wireframe)
    {
        GL.PolygonMode(TriangleFace.FrontAndBack, wireframe ? PolygonMode.Line : PolygonMode.Fill);
    }
}
