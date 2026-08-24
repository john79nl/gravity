using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Gravity
{
    /// <summary>
    /// Full-screen animated splash screen.
    /// Phase flow:
    ///   Idle → Revealing → Active → Collapsing → FlyingOut
    ///   → Thinking → ResultReady → Expanding → Responding
    /// </summary>
    public sealed class SplashScreen : Form
    {
        // ─── Phases ────────────────────────────────────────────────────────────
        private enum Phase
        {
            Idle,         // Big circle center, pulsing
            Revealing,    // Input bar fades/slides in below circle
            Active,       // User can type
            Collapsing,   // Input shrinks into a small request circle beside main
            FlyingOut,    // Both circles fly to top-left corner
            Thinking,     // Corner: big anchor + small spinning (blue)
            ResultReady,  // Small circle turns green, pulses → click to open
            Expanding,    // Panel expands from corner
            Responding    // Streamed text visible
        }
        private Phase _phase = Phase.Idle;

        // ─── Geometry ─────────────────────────────────────────────────────────
        private const float BTN_R_IDLE   = 62f;   // main circle idle radius
        private const float BTN_R_CORNER = 26f;   // main circle radius in corner
        private const float REQ_R_FULL   = 20f;   // request circle beside main
        private const float REQ_R_CORNER = 13f;   // request circle radius in corner

        private const int   BAR_W   = 520;
        private const int   BAR_H   = 58;
        private const float BAR_RAD = 29f;
        private const int   BAR_GAP = 26;         // gap between circle bottom and bar top

        // Corner anchor positions
        private const float CORNER_MX = 54f;
        private const float CORNER_MY = 54f;
        private const float CORNER_RX = 54f + 26f + 10f + 13f;  // = 103
        private const float CORNER_RY = 54f;

        // Response panel
        private const float PANEL_FRAC = 0.60f;
        private const float PANEL_PAD  = 28f;

        // ─── Animated values ───────────────────────────────────────────────────
        private float _btnRadius, _btnCx, _btnCy;   // main circle
        private float _reqRadius, _reqCx,  _reqCy;  // request circle
        private float _reqAlpha;                     // 0-1 opacity of request circle
        private float _reqGreenT;                    // 0-1  blue→green transition
        private float _pulse;                        // breathing glow
        private float _resultPulse;                  // green pulse when ready
        private float _barAlpha;                     // search bar opacity
        private float _barCollapseW;                 // bar width during collapse
        private float _flyT;                         // fly-out progress
        private float _spinnerAngle;                 // orbit angle
        private float _panelT, _panelAlpha;          // result panel
        private float _continueAlpha;

        // Saved fly-start positions (captured when Collapsing ends)
        private float _flyStartBtnCx, _flyStartBtnCy;
        private float _flyStartReqCx, _flyStartReqCy;

        // ─── Layout references ─────────────────────────────────────────────────
        private float _cx, _cy, _btnIdleY;
        private RectangleF _barRect, _sendRect;
        private RectangleF _panelRect, _continueBtnRect;

        // ─── Hover / interaction state ─────────────────────────────────────────
        private bool _btnHov, _sendHov, _reqHov, _continueBtnHov;

        // ─── Timer ────────────────────────────────────────────────────────────
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
        private double _elapsed;

        private const double MS_REVEAL   = 280;
        private const double MS_COLLAPSE = 480;
        private const double MS_FLY      = 600;
        private const double MS_GREEN    = 550;
        private const double MS_EXPAND   = 380;

        // ─── Stars ────────────────────────────────────────────────────────────
        private readonly (float X, float Y, float R, byte A)[] _stars;

        // ─── TextBox input ────────────────────────────────────────────────────
        private readonly TextBox _input;

        // ─── Streamed response ────────────────────────────────────────────────
        private readonly System.Text.StringBuilder _streamedText = new();
        private readonly object _textLock = new();
        private int    _visibleChars;
        private int    _targetChars;
        private string _submittedInput = string.Empty;
        private bool   _isThinking     = true;
        private const int CHARS_PER_TICK = 3;

        // ─── Public events / API ──────────────────────────────────────────────
        public event EventHandler<string>? SubmitReady;
        public string UserInput => _submittedInput.Length > 0 ? _submittedInput : _input.Text.Trim();

        public void AppendStreamedText(string token)
        {
            lock (_textLock) { _streamedText.Append(token); _targetChars = _streamedText.Length; }
        }

        public void SetThinkingDone() => _isThinking = false;

        // ──────────────────────────────────────────────────────────────────────
        public SplashScreen()
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState     = FormWindowState.Maximized;
            DoubleBuffered  = true;
            BackColor       = Color.FromArgb(5, 7, 28);
            StartPosition   = FormStartPosition.Manual;

            var rng = new Random(42);
            _stars = new (float, float, float, byte)[130];
            for (int i = 0; i < _stars.Length; i++)
                _stars[i] = (rng.Next(1920), rng.Next(1080),
                             (float)(rng.NextDouble() * 1.8 + 0.3),
                             (byte)rng.Next(50, 200));

            _input = new TextBox
            {
                BorderStyle     = BorderStyle.None,
                Font            = new Font("Segoe UI", 13.5f),
                BackColor       = Color.FromArgb(12, 22, 58),
                ForeColor       = Color.FromArgb(215, 228, 255),
                PlaceholderText = "What would you like to do?",
                Visible         = false,
                TabIndex        = 0,
            };
            _input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) SubmitInput(); };
            Controls.Add(_input);

            _timer.Tick += OnTick;
            Load         += OnLoad;
            Resize       += (_, _) => { Recalc(); Invalidate(); };
        }

        // ─── Load ─────────────────────────────────────────────────────────────
        private void OnLoad(object? s, EventArgs e)
        {
            Bounds = Screen.PrimaryScreen?.Bounds ?? Bounds;
            Recalc();
            _timer.Start();
        }

        // ─── Layout ───────────────────────────────────────────────────────────
        private void Recalc()
        {
            _cx        = ClientSize.Width  / 2f;
            _cy        = ClientSize.Height / 2f;
            _btnIdleY  = _cy;

            // Reset main circle only in early phases
            if (_phase <= Phase.Active)
            {
                _btnRadius = BTN_R_IDLE;
                _btnCx     = _cx;
                _btnCy     = _btnIdleY;
            }

            RecalcBar();
            PlaceInput();
            RecalcPanel();
        }

        private void RecalcBar()
        {
            float bw = (_phase == Phase.Collapsing) ? _barCollapseW : BAR_W;
            float bx = _cx - bw / 2f;
            float by = _btnIdleY + BTN_R_IDLE + BAR_GAP;
            _barRect = new RectangleF(bx, by, bw, BAR_H);

            float sr  = BAR_H / 2f - 8f;
            float scx = _barRect.Right - BAR_RAD;
            float scy = _barRect.Top + BAR_H / 2f;
            _sendRect = new RectangleF(scx - sr, scy - sr, sr * 2, sr * 2);
        }

        private void PlaceInput()
        {
            int pad = (int)BAR_RAD + 6;
            int ix  = (int)_barRect.Left + pad;
            int iw  = (int)_sendRect.Left - ix - 10;
            int iy  = (int)(_barRect.Top + (_barRect.Height - _input.Height) / 2f);
            _input.SetBounds(ix, iy, iw, _input.Height);
        }

        private void RecalcPanel()
        {
            float panelW = ClientSize.Width * PANEL_FRAC;
            float panelX = ClientSize.Width - panelW;
            _panelRect = new RectangleF(panelX, 0, panelW, ClientSize.Height);

            float btnW = 200f, btnH = 48f;
            _continueBtnRect = new RectangleF(
                _panelRect.Right - btnW - PANEL_PAD,
                _panelRect.Bottom - btnH - PANEL_PAD,
                btnW, btnH);
        }

        // ─── Animation tick ───────────────────────────────────────────────────
        private void OnTick(object? s, EventArgs e)
        {
            _elapsed += _timer.Interval;

            switch (_phase)
            {
                // ── 1. Idle ──────────────────────────────────────────────────
                case Phase.Idle:
                    _pulse     = (float)((Math.Sin(_elapsed / 800.0) + 1.0) * 0.5);
                    _btnRadius = BTN_R_IDLE;
                    _btnCx     = _cx;
                    _btnCy     = _btnIdleY;
                    break;

                // ── 2. Revealing ─────────────────────────────────────────────
                case Phase.Revealing:
                {
                    float t = Clamp01((float)(_elapsed / MS_REVEAL));
                    _barAlpha    = EaseOutCubic(t);
                    _barCollapseW = BAR_W;
                    RecalcBar();

                    if (!_input.Visible && _barAlpha > 0.08f)
                    {
                        PlaceInput();
                        _input.Visible = true;
                    }
                    if (t >= 1f) { _barAlpha = 1f; _phase = Phase.Active; _input.Focus(); }
                    break;
                }

                // ── 3. Active ────────────────────────────────────────────────
                case Phase.Active:
                    break;

                // ── 4. Collapsing ─────────────────────────────────────────────
                // Bar shrinks from BAR_W → 0, simultaneously a small circle
                // emerges beside the main circle.
                case Phase.Collapsing:
                {
                    float t  = Clamp01((float)(_elapsed / MS_COLLAPSE));
                    float te = EaseInOutCubic(t);

                    // Bar width collapses to zero
                    _barCollapseW = Lerp(BAR_W, 0f, te);
                    _barAlpha     = Lerp(1f, 0f, EaseOutCubic(t));
                    RecalcBar();

                    // Request circle grows from the bar's center outward,
                    // sliding toward the position beside the main circle.
                    float barCentX = _cx;
                    float barCentY = _btnIdleY + BTN_R_IDLE + BAR_GAP + BAR_H / 2f;
                    float targetX  = _cx + BTN_R_IDLE + 14f + REQ_R_FULL;
                    float targetY  = _btnIdleY;

                    _reqCx     = Lerp(barCentX, targetX, te);
                    _reqCy     = Lerp(barCentY, targetY, te);
                    _reqRadius = Lerp(0f, REQ_R_FULL, EaseOutCubic(t));
                    _reqAlpha  = EaseOutCubic(t);

                    if (t >= 0.35f) _input.Visible = false;

                    if (t >= 1f)
                    {
                        _barAlpha = 0f;
                        // Save starting positions for FlyingOut
                        _flyStartBtnCx = _cx;
                        _flyStartBtnCy = _btnIdleY;
                        _flyStartReqCx = _cx + BTN_R_IDLE + 14f + REQ_R_FULL;
                        _flyStartReqCy = _btnIdleY;
                        BeginPhase(Phase.FlyingOut);
                    }
                    break;
                }

                // ── 5. FlyingOut ─────────────────────────────────────────────
                // Main circle + request circle fly together to top-left corner.
                case Phase.FlyingOut:
                {
                    float t  = Clamp01((float)(_elapsed / MS_FLY));
                    float te = EaseInOutCubic(t);
                    _flyT = te;

                    _btnCx     = Lerp(_flyStartBtnCx, CORNER_MX, te);
                    _btnCy     = Lerp(_flyStartBtnCy, CORNER_MY, te);
                    _btnRadius = Lerp(BTN_R_IDLE,  BTN_R_CORNER, te);

                    _reqCx     = Lerp(_flyStartReqCx, CORNER_RX, te);
                    _reqCy     = Lerp(_flyStartReqCy, CORNER_RY, te);
                    _reqRadius = Lerp(REQ_R_FULL, REQ_R_CORNER, te);

                    if (t >= 1f) BeginPhase(Phase.Thinking);
                    break;
                }

                // ── 6. Thinking ───────────────────────────────────────────────
                // Small circle orbits (blue). Typewriter runs in background.
                case Phase.Thinking:
                {
                    _btnCx = CORNER_MX; _btnCy = CORNER_MY; _btnRadius = BTN_R_CORNER;
                    _reqCx = CORNER_RX; _reqCy = CORNER_RY; _reqRadius = REQ_R_CORNER;

                    _spinnerAngle = (_spinnerAngle + 5.5f) % 360f;
                    _pulse        = (float)((Math.Sin(_elapsed / 700.0) + 1.0) * 0.5);

                    int target;
                    lock (_textLock) target = _targetChars;
                    if (_visibleChars < target)
                        _visibleChars = Math.Min(_visibleChars + CHARS_PER_TICK, target);

                    if (!_isThinking && target > 0 && _visibleChars >= target)
                        BeginPhase(Phase.ResultReady);
                    break;
                }

                // ── 7. ResultReady ────────────────────────────────────────────
                // Small circle transitions blue→green, pulses. Click to open.
                case Phase.ResultReady:
                {
                    _btnCx = CORNER_MX; _btnCy = CORNER_MY; _btnRadius = BTN_R_CORNER;
                    _reqCx = CORNER_RX; _reqCy = CORNER_RY; _reqRadius = REQ_R_CORNER;

                    float t = Clamp01((float)(_elapsed / MS_GREEN));
                    _reqGreenT   = EaseOutCubic(t);
                    _resultPulse = (float)((Math.Sin(_elapsed / 500.0) + 1.0) * 0.5);
                    _spinnerAngle = (_spinnerAngle + 1.8f) % 360f; // slow trailing glow
                    break;
                }

                // ── 8. Expanding ──────────────────────────────────────────────
                // Result panel slides in from the right.
                case Phase.Expanding:
                {
                    float t  = Clamp01((float)(_elapsed / MS_EXPAND));
                    float te = EaseOutCubic(t);
                    _panelT     = te;
                    _panelAlpha = te;
                    if (t >= 1f) { _panelT = 1f; _panelAlpha = 1f; _phase = Phase.Responding; }
                    break;
                }

                // ── 9. Responding ─────────────────────────────────────────────
                case Phase.Responding:
                {
                    int target2;
                    lock (_textLock) target2 = _targetChars;
                    if (_visibleChars < target2)
                        _visibleChars = Math.Min(_visibleChars + CHARS_PER_TICK, target2);

                    _spinnerAngle = (_spinnerAngle + 3f) % 360f;

                    if (!_isThinking && _visibleChars >= target2)
                        _continueAlpha = Math.Min(_continueAlpha + 0.025f, 1f);
                    break;
                }
            }

            Invalidate();
        }

        private void BeginPhase(Phase next) { _phase = next; _elapsed = 0; }

        // ─── Painting ─────────────────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            PaintBackground(g);
            PaintStars(g);

            // Result panel (behind everything except corner circles)
            if (_phase >= Phase.Expanding)
                PaintResponsePanel(g);

            // Search bar
            if (_barAlpha > 0.01f && _phase >= Phase.Revealing && _phase <= Phase.Collapsing)
                PaintSearchBar(g);

            // Main circle
            PaintMainCircle(g);

            // Request circle (from Collapsing onward)
            if (_phase >= Phase.Collapsing)
                PaintRequestCircle(g);

            // Labels (title / tagline)
            PaintLabels(g);
        }

        // ── Background ────────────────────────────────────────────────────────
        private void PaintBackground(Graphics g)
        {
            using var bg = new LinearGradientBrush(
                new Point(0, 0), new Point(0, ClientSize.Height),
                Color.FromArgb(5, 7, 28), Color.FromArgb(3, 16, 50));
            g.FillRectangle(bg, ClientRectangle);

            using var bloom = new GraphicsPath();
            bloom.AddEllipse(_cx - 720, _cy - 440, 1440, 880);
            using var rb = new PathGradientBrush(bloom);
            rb.CenterColor    = Color.FromArgb(18, 35, 100, 190);
            rb.SurroundColors = new[] { Color.Transparent };
            g.FillPath(rb, bloom);
        }

        // ── Stars ─────────────────────────────────────────────────────────────
        private void PaintStars(Graphics g)
        {
            float sx = ClientSize.Width / 1920f, sy = ClientSize.Height / 1080f;
            foreach (var (x, y, r, a) in _stars)
            {
                using var b = new SolidBrush(Color.FromArgb(a, 175, 200, 255));
                g.FillEllipse(b, x * sx - r, y * sy - r, r * 2, r * 2);
            }
        }

        // ── Main circle ───────────────────────────────────────────────────────
        private void PaintMainCircle(Graphics g)
        {
            float r = _btnRadius, cx = _btnCx, cy = _btnCy;

            bool idle = _phase == Phase.Idle;
            float gs  = idle ? (1f + _pulse * 0.22f) : 1f;

            // Glow rings
            for (int ring = 6; ring >= 1; ring--)
            {
                float gr = r * (1.25f + ring * 0.28f) * gs;
                int   ga = Math.Max(0, (int)((7 + _pulse * 5) * (7 - ring)));
                using var gb = new SolidBrush(Color.FromArgb(ga, 60, 120, 255));
                g.FillEllipse(gb, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            var bounds = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using var gp = new GraphicsPath();
            gp.AddEllipse(bounds);
            using var pgr = new PathGradientBrush(gp);
            pgr.CenterPoint    = new PointF(cx - r * 0.18f, cy - r * 0.28f);
            pgr.CenterColor    = _btnHov ? Color.FromArgb(210, 228, 255) : Color.FromArgb(155, 195, 255);
            pgr.SurroundColors = new[] { _btnHov ? Color.FromArgb(85, 148, 255) : Color.FromArgb(40, 95, 228) };
            g.FillPath(pgr, gp);

            using var rim = new Pen(Color.FromArgb(160, 170, 210, 255), 1.5f);
            g.DrawEllipse(rim, bounds);

            // Play triangle — only when circle is big enough
            if (r > BTN_R_CORNER + 8f)
            {
                float hs = r * 0.28f;
                g.FillPolygon(new SolidBrush(Color.FromArgb(230, 255, 255, 255)), new[]
                {
                    new PointF(cx - hs * 0.5f, cy - hs),
                    new PointF(cx + hs,        cy),
                    new PointF(cx - hs * 0.5f, cy + hs),
                });
            }
        }

        // ── Request circle (beside main → corner → result toggle) ─────────────
        private void PaintRequestCircle(Graphics g)
        {
            float r  = _reqRadius;
            float cx = _reqCx, cy = _reqCy;
            int   a  = Clamp((int)(_reqAlpha * 255), 0, 255);
            if (a < 4 || r < 1f) return;

            // Color: blue → green
            int rC = Clamp((int)Lerp(40,  30,  _reqGreenT), 0, 255);
            int gC = Clamp((int)Lerp(95,  210, _reqGreenT), 0, 255);
            int bC = Clamp((int)Lerp(235, 75,  _reqGreenT), 0, 255);

            // Glow
            float glowMult = (_phase == Phase.ResultReady) ? (1f + _resultPulse * 0.5f) : 1f;
            for (int ring = 5; ring >= 1; ring--)
            {
                float gr = r * (1.4f + ring * 0.38f) * glowMult;
                int   ga = Clamp((int)((5 + _resultPulse * 10) * (6 - ring) * _reqAlpha), 0, 255);
                using var gb = new SolidBrush(Color.FromArgb(ga, rC, gC, bC));
                g.FillEllipse(gb, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            var bounds = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using var gp = new GraphicsPath();
            gp.AddEllipse(bounds);
            using var pgr = new PathGradientBrush(gp);
            pgr.CenterColor    = Color.FromArgb(a, Clamp(rC + 80, 0, 255), Clamp(gC + 60, 0, 255), Clamp(bC + 50, 0, 255));
            pgr.SurroundColors = new[] { Color.FromArgb(a, rC, gC, bC) };
            g.FillPath(pgr, gp);

            using var rim = new Pen(Color.FromArgb(a / 2, Clamp(rC + 100, 0, 255), Clamp(gC + 80, 0, 255), Clamp(bC + 80, 0, 255)), 1.2f);
            g.DrawEllipse(rim, bounds);

            // Orbiting dots while thinking or fading on ready
            if (_phase == Phase.Thinking || _phase == Phase.ResultReady || _phase == Phase.Responding)
                PaintOrbitDots(g, cx, cy, r, a, rC, gC, bC);

            // "Click" hint when result is ready
            if (_phase == Phase.ResultReady && _reqGreenT > 0.7f)
            {
                int hintA = Clamp((int)((_reqGreenT - 0.7f) / 0.3f * 200 * (0.6f + _resultPulse * 0.4f)), 0, 255);
                using var hintFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
                string hint = "Click";
                var hsz = g.MeasureString(hint, hintFont);
                using var hintBr = new SolidBrush(Color.FromArgb(hintA, 180, 255, 200));
                g.DrawString(hint, hintFont, hintBr,
                    cx - hsz.Width / 2f,
                    cy + r + 7f);
            }
        }

        // ── Orbiting dots around request circle ───────────────────────────────
        private void PaintOrbitDots(Graphics g, float cx, float cy, float r, int alpha, int rC, int gC, int bC)
        {
            float orbitR = r + ((_phase == Phase.Thinking) ? 9f : 6f);
            int   dots   = 8;
            float speed  = (_phase == Phase.Responding) ? 3f : (_phase == Phase.ResultReady) ? 1.5f : 5.5f;

            for (int i = 0; i < dots; i++)
            {
                float angle = _spinnerAngle + i * (360f / dots);
                float rad   = angle * MathF.PI / 180f;
                float dotX  = cx + MathF.Cos(rad) * orbitR;
                float dotY  = cy + MathF.Sin(rad) * orbitR;
                float dotR  = (_phase == Phase.Thinking) ? 3.2f : 2.2f;
                float frac  = (float)i / dots;
                int   dotA  = Clamp((int)(alpha * (1f - frac * 0.82f)), 0, 255);

                using var dotBr = new SolidBrush(Color.FromArgb(dotA, rC, gC, bC));
                g.FillEllipse(dotBr, dotX - dotR, dotY - dotR, dotR * 2, dotR * 2);
            }
        }

        // ── Search bar ────────────────────────────────────────────────────────
        private void PaintSearchBar(Graphics g)
        {
            int a = Clamp((int)(_barAlpha * 255), 0, 255);
            if (a < 4 || _barCollapseW < 4f) return;

            using var pillPath = RoundedRect(_barRect, Math.Min(BAR_RAD, _barCollapseW / 2f));

            // Shadow
            var shadow = _barRect; shadow.Inflate(12, 8);
            using var shadowPath = RoundedRect(shadow, BAR_RAD + 8);
            using var shadowBr   = new SolidBrush(Color.FromArgb(a / 5, 20, 60, 180));
            g.FillPath(shadowBr, shadowPath);

            using var pillBr = new SolidBrush(Color.FromArgb(a, 12, 22, 58));
            g.FillPath(pillBr, pillPath);

            using var hlBr = new SolidBrush(Color.FromArgb(a / 4, 160, 200, 255));
            float hlX = _barRect.Left + Math.Min(BAR_RAD, _barCollapseW / 2f);
            g.FillRectangle(hlBr, hlX, _barRect.Top, Math.Max(0, _barRect.Width - Math.Min(BAR_RAD, _barCollapseW / 2f) * 2), 1.5f);

            using var pillPen = new Pen(Color.FromArgb(a, 50, 90, 200), 1.5f);
            g.DrawPath(pillPen, pillPath);

            // Send button — only show when bar is wide enough
            if (_phase == Phase.Active || (_phase == Phase.Revealing) || (_phase == Phase.Collapsing && _barCollapseW > BAR_W * 0.45f))
            {
                bool sh = _sendHov;
                using var sBr = new SolidBrush(Color.FromArgb(a, sh ? 88 : 52, sh ? 152 : 108, 255));
                g.FillEllipse(sBr, _sendRect);
                using var sPen = new Pen(Color.FromArgb(a / 2, 140, 180, 255), 1f);
                g.DrawEllipse(sPen, _sendRect);

                float scx = _sendRect.Left + _sendRect.Width  / 2f;
                float scy = _sendRect.Top  + _sendRect.Height / 2f;
                float sc  = _sendRect.Width / 2f * 0.36f;
                using var arBr = new SolidBrush(Color.FromArgb(a, 255, 255, 255));
                g.FillPolygon(arBr, new[]
                {
                    new PointF(scx - sc * 0.5f, scy - sc),
                    new PointF(scx + sc,        scy),
                    new PointF(scx - sc * 0.5f, scy + sc),
                });
            }
        }

        // ── Response panel ────────────────────────────────────────────────────
        private void PaintResponsePanel(Graphics g)
        {
            int   a      = Clamp((int)(_panelAlpha * 255), 0, 255);
            float openW  = _panelRect.Width * _panelT;
            float panelX = _panelRect.Right - openW;
            if (openW < 2f) return;

            var panelBounds = new RectangleF(panelX, 0, openW, ClientSize.Height);

            using var shadowBr = new LinearGradientBrush(
                new PointF(panelX - 50, 0), new PointF(panelX, 0),
                Color.Transparent, Color.FromArgb(a / 4, 15, 40, 120));
            g.FillRectangle(shadowBr, panelX - 50, 0, 50, ClientSize.Height);

            using var panelBr = new SolidBrush(Color.FromArgb(Math.Min(a, 232), 8, 14, 40));
            g.FillRectangle(panelBr, panelBounds);

            using var edgePen = new Pen(Color.FromArgb(a / 2, 55, 100, 220), 2f);
            g.DrawLine(edgePen, panelX, 0, panelX, ClientSize.Height);

            using var topBr = new LinearGradientBrush(
                new PointF(panelX, 0), new PointF(panelX, 90),
                Color.FromArgb(a / 3, 50, 100, 240), Color.Transparent);
            g.FillRectangle(topBr, panelX, 0, openW, 90);

            if (_panelT > 0.85f && _phase == Phase.Responding)
            {
                float ca01 = (_panelT - 0.85f) / 0.15f;
                int   ca   = Clamp((int)(ca01 * 255), 0, 255);
                PaintStreamedText(g, ca, panelX);
                if (_continueAlpha > 0.01f)
                    PaintContinueButton(g, (int)(_continueAlpha * ca));
            }
        }

        private void PaintStreamedText(Graphics g, int alpha, float panelX)
        {
            string snapshot;
            lock (_textLock) snapshot = _streamedText.ToString();
            if (snapshot.Length == 0) return;

            string visible = snapshot.Length <= _visibleChars ? snapshot : snapshot[.._visibleChars];
            using var textFont = new Font("Segoe UI", 11.5f, FontStyle.Regular, GraphicsUnit.Point);
            using var textBr   = new SolidBrush(Color.FromArgb(alpha, 210, 225, 255));
            g.DrawString(visible, textFont, textBr,
                new RectangleF(panelX + PANEL_PAD, 82f,
                               _panelRect.Width - PANEL_PAD * 2f,
                               _continueBtnRect.Top - 82f - PANEL_PAD));
        }

        private void PaintContinueButton(Graphics g, int alpha)
        {
            bool hov = _continueBtnHov;
            using var btnPath = RoundedRect(_continueBtnRect, 24f);
            using var btnBr   = new SolidBrush(Color.FromArgb(alpha, hov ? 65 : 40, hov ? 130 : 100, 255));
            g.FillPath(btnBr, btnPath);
            using var border = new Pen(Color.FromArgb(alpha / 2, 120, 170, 255), 1.5f);
            g.DrawPath(border, btnPath);

            using var lblFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            const string lbl = "Open Gravity  →";
            var sz = g.MeasureString(lbl, lblFont);
            using var lblBr = new SolidBrush(Color.FromArgb(alpha, 230, 240, 255));
            g.DrawString(lbl, lblFont, lblBr,
                _continueBtnRect.Left + (_continueBtnRect.Width  - sz.Width)  / 2f,
                _continueBtnRect.Top  + (_continueBtnRect.Height - sz.Height) / 2f);
        }

        // ── Title / tagline ───────────────────────────────────────────────────
        private void PaintLabels(Graphics g)
        {
            // Title fades out during flyout
            if (_phase > Phase.FlyingOut) return;

            float labelY = _btnCy - _btnRadius - 52f;
            if (labelY < 18f) labelY = 18f;

            using var titleFont = new Font("Segoe UI Light", 28f, FontStyle.Regular, GraphicsUnit.Point);
            string title = "Gravity";
            var tsz = g.MeasureString(title, titleFont);
            int ta = _phase == Phase.FlyingOut
                ? Clamp((int)((1f - _flyT) * 210), 0, 255)
                : 210;
            using var titleBr = new SolidBrush(Color.FromArgb(ta, 200, 220, 255));
            g.DrawString(title, titleFont, titleBr, _btnCx - tsz.Width / 2f, labelY);

            // Tagline — idle only
            if (_phase == Phase.Idle)
            {
                int tagA = Clamp((int)(155 + _pulse * 65), 0, 255);
                using var tagFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
                const string tag = "Click to begin";
                var tgsz = g.MeasureString(tag, tagFont);
                using var tagBr = new SolidBrush(Color.FromArgb(tagA, 120, 150, 220));
                g.DrawString(tag, tagFont, tagBr,
                    _cx - tgsz.Width / 2f,
                    _btnIdleY + BTN_R_IDLE + 18f);
            }
        }

        // ─── Mouse interaction ────────────────────────────────────────────────
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool bh = (_phase == Phase.Idle || _phase == Phase.Revealing || _phase == Phase.Active)
                      && InCircle(e.Location, _btnCx, _btnCy, _btnRadius);
            bool sh = _phase == Phase.Active
                      && _sendRect.Contains(e.Location);
            bool rh = _phase == Phase.ResultReady
                      && InCircle(e.Location, _reqCx, _reqCy, _reqRadius + 9f);
            bool ch = _phase == Phase.Responding && _continueAlpha > 0.5f
                      && _continueBtnRect.Contains(e.Location);

            if (bh != _btnHov || sh != _sendHov || rh != _reqHov || ch != _continueBtnHov)
            {
                _btnHov = bh; _sendHov = sh; _reqHov = rh; _continueBtnHov = ch;
                Cursor = (bh || sh || rh || ch) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;

            // Idle: click main circle → reveal input bar
            if (_phase == Phase.Idle && InCircle(e.Location, _btnCx, _btnCy, _btnRadius))
            {
                _barCollapseW = BAR_W;
                BeginPhase(Phase.Revealing);
            }
            // Active: click send button → start collapsing
            else if (_phase == Phase.Active && _sendRect.Contains(e.Location))
            {
                SubmitInput();
            }
            // Result ready: click green circle → expand result panel
            else if (_phase == Phase.ResultReady && InCircle(e.Location, _reqCx, _reqCy, _reqRadius + 9f))
            {
                BeginPhase(Phase.Expanding);
            }
            // Responding: click "Open Gravity" button
            else if (_phase == Phase.Responding && _continueAlpha > 0.5f && _continueBtnRect.Contains(e.Location))
            {
                ContinueToApp();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        }

        // ─── Submit & Continue ────────────────────────────────────────────────
        private void SubmitInput()
        {
            if (string.IsNullOrWhiteSpace(_input.Text)) return;
            _submittedInput = _input.Text.Trim();
            SubmitReady?.Invoke(this, _submittedInput);
            _barCollapseW = BAR_W;
            BeginPhase(Phase.Collapsing);
        }

        private void ContinueToApp()
        {
            _timer.Stop();
            DialogResult = DialogResult.OK;
            Close();
        }

        // ─── Utilities ────────────────────────────────────────────────────────
        private static GraphicsPath RoundedRect(RectangleF r, float rad)
        {
            rad = Math.Max(0.1f, rad);
            var p = new GraphicsPath();
            p.AddArc(r.Left,           r.Top,            rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad*2,  r.Top,            rad * 2, rad * 2, 270, 90);
            p.AddArc(r.Right - rad*2,  r.Bottom - rad*2, rad * 2, rad * 2,   0, 90);
            p.AddArc(r.Left,           r.Bottom - rad*2, rad * 2, rad * 2,  90, 90);
            p.CloseFigure();
            return p;
        }

        private static bool InCircle(Point p, float cx, float cy, float r)
        {
            float dx = p.X - cx, dy = p.Y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Clamp01(float t)                 => Math.Clamp(t, 0f, 1f);
        private static int   Clamp(int v, int lo, int hi)     => Math.Clamp(v, lo, hi);
        private static float EaseOutCubic(float t)            => 1f - MathF.Pow(1f - t, 3f);
        private static float EaseInOutCubic(float t)          => t < 0.5f
            ? 4f * t * t * t
            : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
