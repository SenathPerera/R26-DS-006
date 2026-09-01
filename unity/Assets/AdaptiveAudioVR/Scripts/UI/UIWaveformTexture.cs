using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveAudioVR.UI
{
    [RequireComponent(typeof(RawImage))]
    public class UIWaveformTexture : MonoBehaviour
    {
        [SerializeField] private int textureWidth = 256;
        [SerializeField] private int textureHeight = 64;
        [SerializeField] private int lineThickness = 2;
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0f);
        [SerializeField] private Color waveformColor = Color.white;
        [SerializeField] private Color centerLineColor = new Color(1f, 1f, 1f, 0.15f);

        private RawImage rawImage;
        private Texture2D texture;
        private Color32[] pixels;

        private void Awake()
        {
            Initialize();
        }

        public void SetColors(Color waveColor, Color bgColor)
        {
            waveformColor = waveColor;
            backgroundColor = bgColor;
            Initialize();
        }

        public void SetData(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            Initialize();
            ClearPixels();

            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }

            float visibleRange = Mathf.Max(0.02f, peak * 1.25f);
            SetData(samples, -visibleRange, visibleRange);
        }

        public void SetData(float[] samples, float minValue, float maxValue)
        {
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            Initialize();
            ClearPixels();

            int centerY = Mathf.Clamp(ValueToY(0f, minValue, maxValue), 0, textureHeight - 1);
            DrawHorizontalLine(centerY, centerLineColor);

            int previousX = 0;
            int previousY = ValueToY(samples[0], minValue, maxValue);

            for (int x = 1; x < textureWidth; x++)
            {
                int sampleIndex = Mathf.Clamp(Mathf.RoundToInt((x / (textureWidth - 1f)) * (samples.Length - 1)), 0, samples.Length - 1);
                int sampleY = ValueToY(samples[sampleIndex], minValue, maxValue);
                DrawLine(previousX, previousY, x, sampleY, waveformColor);
                previousX = x;
                previousY = sampleY;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private void Initialize()
        {
            rawImage ??= GetComponent<RawImage>();

            if (texture != null && texture.width == textureWidth && texture.height == textureHeight && pixels != null)
            {
                return;
            }

            texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            pixels = new Color32[textureWidth * textureHeight];
            rawImage.texture = texture;
            rawImage.color = Color.white;
            ClearPixels();
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private void ClearPixels()
        {
            Color32 clear = backgroundColor;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = clear;
            }
        }

        private void DrawHorizontalLine(int y, Color color)
        {
            if (y < 0 || y >= textureHeight)
            {
                return;
            }

            for (int x = 0; x < textureWidth; x++)
            {
                pixels[(y * textureWidth) + x] = color;
            }
        }

        private int ValueToY(float value, float minValue, float maxValue)
        {
            float safeMax = Mathf.Approximately(minValue, maxValue) ? minValue + 1f : maxValue;
            float normalized = Mathf.InverseLerp(minValue, safeMax, value);
            return Mathf.Clamp(Mathf.RoundToInt(normalized * (textureHeight - 1)), 0, textureHeight - 1);
        }

        private void DrawLine(int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                DrawPoint(x0, y0, color);

                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void DrawPoint(int x, int y, Color color)
        {
            int radius = Mathf.Max(1, lineThickness) - 1;
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    int drawX = x + offsetX;
                    int drawY = y + offsetY;

                    if (drawX < 0 || drawX >= textureWidth || drawY < 0 || drawY >= textureHeight)
                    {
                        continue;
                    }

                    pixels[(drawY * textureWidth) + drawX] = color;
                }
            }
        }
    }
}
