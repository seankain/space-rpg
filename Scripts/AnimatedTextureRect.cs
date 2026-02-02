using Godot;
using System;

public enum AnimatedTextureRectDirection
{
    Forward,
    Reverse
}

public partial class AnimatedTextureRect : TextureRect
{
    [Export]
    public SpriteFrames Sprites;
    [Export]
    public string CurrentAnimation = "default";
    [Export]
    public int FrameIndex = 0;
    [Export]
    public bool AutoPlay = false;
    [Export]
    public bool Playing = false;
    [Export]
    public float RefreshRate = 1.0f;
    [Export]
    public double FPS = 30.0d;
    [Export]
    public double FrameDelta = 0.0d;
    [Export]
    public double SpeedScale = 1.0f;

    [Export]
    public bool Reversable = true;
    public AnimatedTextureRectDirection Direction = AnimatedTextureRectDirection.Forward;

    private int AnimationDirection = 1;


    public override void _Ready()
    {
        FPS = Sprites.GetAnimationSpeed(CurrentAnimation);
        RefreshRate = Sprites.GetFrameDuration(CurrentAnimation, FrameIndex);
        if (AutoPlay)
        {
            Play(CurrentAnimation);
        }
        base._Ready();
    }

    public override void _Process(double delta)
    {
        if (!Playing) { return; }
        FrameDelta += (SpeedScale + delta);
        GetAnimationData(CurrentAnimation);
        if (FrameDelta >= RefreshRate / FPS)
        {
            var texture = GetNextFrame();
            this.Texture = (Texture2D)texture;
        }
        FrameDelta = 0;
    }

    public void SetFrame() { }

    private Texture GetNextFrame()
    {
        FrameIndex += AnimationDirection;
        // If it is playing forwards check for running off the end
        if (Direction == AnimatedTextureRectDirection.Forward)
        {
            // If we are running off the end in the forward direction
            if (FrameIndex >= Sprites.GetFrameCount(CurrentAnimation))
            {
                // If reverseable is set to true we want to flip the animation
                // backwards
                if (Reversable)
                {
                    Direction = AnimatedTextureRectDirection.Reverse;
                    AnimationDirection = -1;
                }
                else
                {
                    //If not reversable loop to the beginning
                    FrameIndex = 0;
                }
            }
        }
        else if (Direction == AnimatedTextureRectDirection.Reverse)
        {
            if (FrameIndex <= 0)
            {
                if (Reversable)
                {
                    Direction = AnimatedTextureRectDirection.Forward;
                    AnimationDirection = 1;
                }
                else
                {
                    FrameIndex = Sprites.GetFrameCount(CurrentAnimation) - 1;
                }
            }
        }
        return Sprites.GetFrameTexture(CurrentAnimation, FrameIndex);

    }

    public void Pause()
    {
        Playing = false;
    }

    public void Resume()
    {
        Playing = true;
    }

    private void Play(string animationName)
    {
        FrameIndex = 0;
        FrameDelta = 0.0f;
        CurrentAnimation = animationName;
        GetAnimationData(CurrentAnimation);
        Playing = true;
    }

    private void GetAnimationData(string animationName)
    {
        FPS = Sprites.GetAnimationSpeed(CurrentAnimation);
        RefreshRate = Sprites.GetFrameDuration(CurrentAnimation, FrameIndex);
    }

}
