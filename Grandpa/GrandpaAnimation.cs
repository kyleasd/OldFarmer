using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace OldFarmer;

/// <summary>
/// Plays grandpa's "mining" animation: a one-shot morph (grandpa → whirlwind)
/// followed by a looping cyclone. Leaving mining mode plays the morph
/// backwards (whirlwind → grandpa) before snapping back to the normal sprite.
///
/// Frames come from the Aseprite sheets under <c>assets/animations</c>
/// (both sheets use 44×52 frames). GIFs are not directly loadable by
/// MonoGame, so the PNG sprite sheets are used instead.
/// </summary>
internal sealed class GrandpaAnimation
{
    private const string MorphTexturePath   = "assets/animations/grandpa_morph_sheet.png";
    private const string CycloneTexturePath = "assets/animations/grandpa_cyclone_sheet.png";

    private const int   FrameWidth       = 44;
    private const int   FrameHeight      = 52;
    private const int   MorphFrameCount  = 16;
    private const int   CycloneFrameCount = 12;
    private const float FrameDuration    = 0.06f; // seconds per frame (~16 fps)

    private readonly Texture2D _morphTexture;
    private readonly Texture2D _cycloneTexture;

    private int   _frame;
    private float _timer;
    private bool  _isCyclone;
    private bool  _reversing;
    private bool  _reverseDone;

    public GrandpaAnimation(IModHelper helper)
    {
        _morphTexture   = helper.ModContent.Load<Texture2D>(MorphTexturePath);
        _cycloneTexture = helper.ModContent.Load<Texture2D>(CycloneTexturePath);
        Reset();
    }

    public Texture2D Texture => _isCyclone ? _cycloneTexture : _morphTexture;

    public Rectangle SourceRect { get; private set; }

    public Vector2 Origin => new(FrameWidth / 2f, FrameHeight / 2f);

    /// <summary>True while the morph is being played backwards (cyclone → grandpa).</summary>
    public bool IsReversing => _reversing;

    /// <summary>True once a reverse morph has fully reverted to the grandpa frame.</summary>
    public bool IsReversingDone => _reverseDone;

    /// <summary>Restart from the first morph frame.</summary>
    public void Reset()
    {
        _frame       = 0;
        _timer       = 0f;
        _isCyclone   = false;
        _reversing   = false;
        _reverseDone = false;
        SourceRect   = new Rectangle(0, 0, FrameWidth, FrameHeight);
    }

    /// <summary>
    /// Play the morph backwards (cyclone → grandpa). Starts from the current
    /// morph frame so the transition continues smoothly from wherever the
    /// forward animation was, and is seamless when called from the cyclone loop
    /// (whose first frame matches the last morph frame).
    /// </summary>
    public void PlayReverse()
    {
        _reversing   = true;
        _reverseDone = false;

        if (_isCyclone)
        {
            _isCyclone = false;
            _frame     = MorphFrameCount - 1;
        }

        _timer     = 0f;
        SourceRect = new Rectangle(_frame * FrameWidth, 0, FrameWidth, FrameHeight);
    }

    public void Update()
    {
        if (_reversing)
        {
            UpdateReverse();
            return;
        }

        _timer += (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;

        while (_timer >= FrameDuration)
        {
            _timer -= FrameDuration;
            _frame++;

            int count = _isCyclone ? CycloneFrameCount : MorphFrameCount;
            if (_frame >= count)
            {
                // Morph plays once, then the cyclone loops forever.
                _isCyclone = true;
                _frame     = 0;
            }
        }

        SourceRect = new Rectangle(_frame * FrameWidth, 0, FrameWidth, FrameHeight);
    }

    private void UpdateReverse()
    {
        if (_reverseDone)
            return;

        _timer += (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;

        while (_timer >= FrameDuration)
        {
            _timer -= FrameDuration;
            _frame--;

            if (_frame <= 0)
            {
                _frame       = 0;
                _reverseDone = true;
                break;
            }
        }

        SourceRect = new Rectangle(_frame * FrameWidth, 0, FrameWidth, FrameHeight);
    }
}
