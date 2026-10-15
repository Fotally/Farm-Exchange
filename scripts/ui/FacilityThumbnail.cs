using System;
using Godot;

namespace FarmExchange.UI;

// UI 缩略图内部绘图：只显示纹理，不保存建筑或经营状态。
internal partial class FacilityThumbnail : Control
{
    private Texture2D? _texture;

    internal Texture2D? Texture
    {
        get => _texture;
        set
        {
            if (_texture == value) return;
            _texture = value;
            QueueRedraw();
        }
    }

    public FacilityThumbnail()
    {
        Name = "FacilityThumbnail";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        if (_texture == null) return;
        Vector2 source = _texture.GetSize();
        float factor = Math.Min(Size.X / source.X, Size.Y / source.Y);
        Vector2 art = source * factor;
        DrawTextureRect(_texture, new Rect2(new Vector2((Size.X - art.X) / 2, Size.Y - art.Y), art), false);
    }
}
