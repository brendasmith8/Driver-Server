﻿using System;
using System.Collections;
using System.Collections.Generic;
using NightDriver;

namespace NightDriver
{
    public class SimpleColorFillEffect : LEDEffect
    {
        // Update is called once per frame

        protected CRGB _color;
        protected uint  _everyNth;

        public SimpleColorFillEffect(CRGB color, uint everyNth = 10)
        {
            _everyNth = everyNth;
            _color = color;
        }

        protected override void Render(ILEDGraphics graphics)
        {
            graphics.FillSolid(CRGB.Black);
            for (uint i = 0; i < graphics.DotCount; i+=_everyNth)
                graphics.DrawPixel(i, _color);
        }
    };


    public class RainbowEffect : LEDEffect
    {
        // Update is called once per frame
        protected double _deltaHue;
        protected double _startHue;
        protected double _hueSpeed;

        DateTime _lastDraw = DateTime.UtcNow;

        public RainbowEffect(double deltaHue = 0, double hueSpeed = 5)
        {
            _deltaHue = deltaHue;
            _startHue = 0;
            _hueSpeed = hueSpeed;
        }

        protected override void Render(ILEDGraphics graphics)
        {
            double delta = _hueSpeed * (double)(DateTime.UtcNow - _lastDraw).TotalSeconds;
            _lastDraw = DateTime.UtcNow;
            _startHue = (_startHue + delta);

            // BUGBUG It stymies me as to why one is modulus 360 and the other is 256! (davepl)

            CRGB color = CRGB.HSV2RGB(_startHue % 360);
            if (_deltaHue == 0.0)
            {
                graphics.FillSolid(color);
            }
            else
            {
                graphics.FillRainbow(_startHue % 360, _deltaHue);
                graphics.Blur(3);
            }
            //Console.WriteLine(delta.ToString() + ", : " + _startHue.ToString() + "r = " + color.r + " g = " + color.g + " b = " + color.b);
        }
    };

    public class PaletteFillEffect : LEDEffect
    {
        protected Palette _Palette;

        public double     ColorPerSecond = 15;
        public double     ScrollSpeed = 0;
        public double      EveryNthDot = 5;
        public uint       DotSize;
        
        private double    _startIndex = 0;
        private double     _shiftAmount = 0;

        protected ILEDGraphics _Graphics;

        DateTime _lastDraw = DateTime.UtcNow;

        public PaletteFillEffect(Palette palette, uint everyNth = 5, uint dotSize = 1, double colorSpeed = 1, double ledSpeed = 0)
        {
            DotSize = dotSize;
            _Palette = palette;
            EveryNthDot = everyNth;
            ColorPerSecond = colorSpeed;
            ScrollSpeed = ledSpeed;
        }

        // Update is called once per frame

        protected override void Render(ILEDGraphics graphics)
        {
            graphics.FillSolid(CRGB.Black);

            double secondsElapsed = (DateTime.UtcNow - _lastDraw).TotalSeconds;
            _lastDraw = DateTime.UtcNow;

            _shiftAmount += (double)(secondsElapsed * ScrollSpeed);
            _shiftAmount %= EveryNthDot;

            _startIndex = _startIndex + secondsElapsed * ColorPerSecond;
            double index = _startIndex + _shiftAmount;
            index %= _Palette.FullSize;

            for (double i = _shiftAmount; i < graphics.DotCount; i += EveryNthDot)
            { 
                CRGB c = _Palette.ColorFromPalette((byte)index, 1.0f, true);
                graphics.DrawPixels(i, DotSize, c);
            }
        }   
    }

    public class TestEffect : LEDEffect
    {
        private uint _startIndex;
        private uint _length;
        private CRGB _color;

        public TestEffect(uint startIndex, uint length, CRGB color)
        {
            _startIndex = startIndex;
            _length = length;
            _color = color;
        }

        DateTime _lastDraw = DateTime.UtcNow;

        // Update is called once per frame

        protected override void Render(ILEDGraphics graphics)
        {
            for (uint i = _startIndex; i < _startIndex + _length; i++)
                graphics.DrawPixel(i, _color);

            /*
            uint third = _length / 3;

            for (uint i = _startIndex; i < third + _startIndex; i++)
                graphics.DrawPixel(i, CRGB.Blue);

            for (uint i = third + _startIndex; i < third * 2 + _startIndex; i++)
                graphics.DrawPixel(i, CRGB.Red);

            for (uint i = third * 2 + _startIndex; i < _length + _startIndex; i++)
                graphics.DrawPixel(i, CRGB.Green);
            */

            graphics.DrawPixel(_startIndex, CRGB.White);
            graphics.DrawPixel(_startIndex+1, CRGB.Black);
            graphics.DrawPixel(_startIndex+_length-1, CRGB.White);
            graphics.DrawPixel(_startIndex+_length-2, CRGB.Black);

        }
    }

    


}