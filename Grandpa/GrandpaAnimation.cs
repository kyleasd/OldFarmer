using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace OldFarmer;

/// <summary>
/// Plays grandpa's "mining" animation: a one-shot morph (grandpa → whirlwind)
/// followed by a looping cyclone.
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

    public GrandpaAnimation(IModHelper helper)
    {
        _morphTexture   = helper.ModContent.Load<Texture2D>(MorphTexturePath);
        _cycloneTexture = helper.ModContent.Load<Texture2D>(CycloneTexturePath);
        Reset();
    }

    public Texture2D Texture => _isCyclone ? _cycloneTexture : _morphTexture;

    public Rectangle SourceRect { get; private set; }

    public Vector2 Origin => new(FrameWidth / 2f, FrameHeight / 2f);

    /// <summary>Restart from the first morph frame.</summary>
    public void Reset()
    {
        _frame      = 0;
        _timer      = 0f;
        _isCyclone  = false;
        SourceRect  = new Rectangle(0, 0, FrameWidth, FrameHeight);
    }

    public void Update()
    {
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
}
