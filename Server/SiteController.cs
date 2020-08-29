﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace NightDriver
{
    // ScheduledEffect
    //
    // An LED effect with scheduling

    public class ScheduledEffect
    {
        public const DayOfWeek WeekEnds = DayOfWeek.Saturday | DayOfWeek.Sunday;
        public const DayOfWeek WeekDays = DayOfWeek.Monday | DayOfWeek.Tuesday | DayOfWeek.Wednesday | DayOfWeek.Thursday | DayOfWeek.Friday;
        public const DayOfWeek AllDays = WeekDays | WeekEnds;

        protected LEDEffect _LEDEffect;
        protected DayOfWeek _DaysOfWeeks;
        protected uint _StartHour;
        protected uint _EndHour;
        protected uint _StartMinute;
        protected uint _EndMinute;

        public ScheduledEffect(DayOfWeek daysOfWeek, uint startHour, uint endHour, LEDEffect ledEffect, uint startMinute = 0, uint endMinute = 60)
        {
            _DaysOfWeeks = daysOfWeek;
            _LEDEffect = ledEffect;
            _StartHour = startHour;
            _EndHour = endHour;
        }

        public bool ShouldEffectRunNow
        {
            get
            {
                if (_DaysOfWeeks.HasFlag(DateTime.Now.DayOfWeek))
                    if (DateTime.Now.Hour > _StartHour || DateTime.Now.Hour == _StartHour && DateTime.Now.Minute >= _StartMinute)
                        if (DateTime.Now.Hour < _EndHour || DateTime.Now.Hour == _EndHour && DateTime.Now.Minute <= _EndMinute)
                            return true;

                return false;
            }
        }

        public uint MinutesRunning
        {
            get
            {
                uint c = 0;
                if (DateTime.Now.Hour > _StartHour)
                    c += ((uint)DateTime.Now.Hour - _StartHour) * 60;
                if (DateTime.Now.Minute >= _StartMinute)
                    c += ((uint)DateTime.Now.Minute - _StartMinute);
                return c;
            }
        }

        public LEDEffect LEDEffect
        {
            get
            {
                return _LEDEffect;
            }
        }
    }

    // Location
    //
    // A "site" is a set of one or more LED strip controllers and the effects that will run on them.  It
    // implements the "GraphicsBase" interface so that the effects can draw upon the "site" as a whole,
    // and it is later divied up to the various controllers.  So if you have 4000 LEDs, you might have
    // four strips with 1000 LEDs each, for example.  Combined with a list of effects, they consitute a site.

    public abstract class Location : GraphicsBase
    {
        protected DateTime StartTime;
        protected System.Threading.Thread _Thread;
        protected abstract CRGB[] LEDs { get; }
        public abstract LightStrip[] LightStrips { get; }
        public abstract ScheduledEffect[] LEDEffects { get; }

        public Location()
        {
            StartTime = DateTime.Now;
        }

        public int FramesPerSecond
        {
            get; set;
        } = 22;

        protected int SecondsPerEffect
        {
            get
            {
                return 30;
            }
        }

        public uint SpareTime
        {
            get;
            set;
        } = 1000;

        public static uint MinimumSpareTime => (uint)ConsoleApp.g_AllSites.Min(location => location.SpareTime);

        // If we were certain that every pixel would get touched, and hence created, we wouldn't need to init them, but to
        // be safe, we init them all to a default pixel value (like magenta)

        protected static T[] InitializePixels<T>(int length) where T : new()
        {
            T[] array = new T[length];
            for (int i = 0; i < length; ++i)
            {
                array[i] = new T();
            }

            return array;
        }

        void WorkerDrawAndSendLoop()
        {
            DateTime lastSpareTimeReset = DateTime.UtcNow;

            for (;;)
            {
                DateTime timeStart = DateTime.UtcNow;

                DrawAndEnqueueAll();

                uint ms = (uint)(1000 * (1.0f / FramesPerSecond));
                TimeSpan delay = TimeSpan.FromMilliseconds(ms) - (DateTime.UtcNow - timeStart);
                if (delay.TotalMilliseconds > 0)
                {
                    Thread.Sleep(delay);
                }
                else
                {
                    ConsoleApp.Stats.WriteLine(this.GetType().Name + " dropped Frame by " + delay.TotalMilliseconds);
                    Thread.Sleep(10);
                }

                uint spare = (uint)(delay.TotalMilliseconds <= 0 ? 0 : delay.TotalMilliseconds);
                SpareTime = Math.Min(SpareTime, spare);

                ConsoleApp.Stats.SpareMilisecondsPerFrame = (uint)delay.TotalMilliseconds;

                if ((DateTime.UtcNow - lastSpareTimeReset).TotalSeconds > 1)
                {
                    SpareTime = 1000;
                    lastSpareTimeReset = DateTime.UtcNow;
                }
            }
        }

        public void StartWorkerThread()
        {
            foreach (var strip in LightStrips)
                strip.Location = this;


            _Thread = new Thread(WorkerDrawAndSendLoop);
            _Thread.IsBackground = true;
            _Thread.Priority = ThreadPriority.BelowNormal;
            _Thread.Start();
        }

        public string CurrentEffectName
        {
            get;
            private set;
        } = "[None]";

        public void DrawAndEnqueueAll()
        {

            DateTime timeStart2 = DateTime.UtcNow;

            var enabledEffects = LEDEffects.Where(effect => effect.ShouldEffectRunNow == true);
            var effectCount = enabledEffects.Count();
            if (effectCount > 0)
            {
                int iEffect = (int)((DateTime.Now - StartTime).TotalSeconds / SecondsPerEffect);
                iEffect %= effectCount;
                enabledEffects.ElementAt(iEffect).LEDEffect.DrawFrame(this);
                CurrentEffectName = enabledEffects.ElementAt(iEffect).LEDEffect.GetType().Name;
                if ((DateTime.UtcNow - timeStart2).TotalSeconds > 0.25)
                    ConsoleApp.Stats.WriteLine("MAIN3 DELAY");
            }

            if ((DateTime.UtcNow - timeStart2).TotalSeconds > 0.25)
                ConsoleApp.Stats.WriteLine("MAIN2 DELAY");

            DateTime timeStart = DateTime.UtcNow;

            foreach (var controller in LightStrips)
                if (controller.ReadyForData)
                    controller.CompressAndEnqueueData(LEDs, timeStart);

            if ((DateTime.UtcNow - timeStart).TotalSeconds > 0.25)
                ConsoleApp.Stats.WriteLine("MAIN1 DELAY");
        }

        public override uint Width
        {
            get { return (uint)LEDs.Length; }
        }

        public override uint Height
        {
            get { return 1; }
        }

        public override uint LEDCount
        {
            get { return Width * Height; }
        }

        protected uint GetPixelIndex(uint x, uint y)
        {
            return (y * Width) + x;
        }

        protected void SetPixel(uint x, uint y, CRGB color)
        {

            LEDs[GetPixelIndex(x, Height - 1 - y)] = color;
        }

        protected void SetPixel(uint x, CRGB color)
        {
            LEDs[x] = color;
        }

        protected CRGB GetPixel(uint x)
        {
            if (x < 0 || x >= Width)
                return CRGB.Black;

            return LEDs[GetPixelIndex(x, 0)];
        }

        public override CRGB GetPixel(uint x, uint y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return CRGB.Black;

            return LEDs[GetPixelIndex(x, y)];
        }

        public override void DrawPixels(double fPos, double count, CRGB color)
        {
            double availFirstPixel = 1 - (fPos - (uint)(fPos));
            double amtFirstPixel = Math.Min(availFirstPixel, count);
            count = Math.Min(count, DotCount-fPos);
            if (fPos >= 0 && fPos < DotCount)
                BlendPixel((uint)fPos, color.fadeToBlackBy(1.0 - amtFirstPixel));

            fPos += amtFirstPixel;
            //fPos %= DotCount;
            count -= amtFirstPixel;

            while (count >= 1.0)
            {
                if (fPos >= 0 && fPos < DotCount)
                {
                    BlendPixel((uint)fPos, color);
                    count -= 1.0;
                }
                fPos += 1.0;
            }

            if (count > 0.0)
            {
                if (fPos >= 0 && fPos < DotCount)
                    BlendPixel((uint)fPos, color.fadeToBlackBy(1.0 - count));
            }
        }

        public override void DrawPixel(uint x, CRGB color)
        {
            SetPixel(x, color);
        }

        public override void DrawPixel(uint x, uint y, CRGB color)
        {
            SetPixel(x, y, color);
        }

        public override void BlendPixel(uint x, CRGB color)
        {
            CRGB c1 = GetPixel(x);
            SetPixel(x, c1 + color);
        }
    };

    // EffectsDatabase
    //
    // A static database of some predefined effects

    public static class EffectsDatabase
    {
        public static LEDEffect ColorCycleTube => new PaletteEffect(Palette.Rainbow)
        {
            _Density = 0,
            _EveryNthDot = 1,
            _DotSize = 1,
            _LEDColorPerSecond = 1.75,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect WhitePointLights => new SimpleColorFillEffect(new CRGB(246, 200, 160));

        public static LEDEffect QuietBlueStars => new StarEffect<ColorStar>
        {
            Blend = true,
            NewStarProbability = 2.25,
            StarPreignitonTime = 0.25,
            StarIgnition = 0.0, 
            StarHoldTime = 2.0,
            StarFadeTime = 1.0,
            StarSize = 1,
            MaxSpeed = 0,
            BaseColorSpeed = 0.1,
            ColorSpeed = 0.0,
            RandomStartColor = false,
            RandomStarColorSpeed = false,
        };

        public static LEDEffect QuietColorStars => new StarEffect<PaletteStar>
        {
            Blend = true,
            NewStarProbability = 1.25,
            StarPreignitonTime = 0.25,
            StarIgnition = 0.0,
            StarHoldTime = 2.0,
            StarFadeTime = 1.0,
            StarSize = 1,
            MaxSpeed = 1,
            ColorSpeed = 0,
            Palette = new Palette(CRGB.ChristmasLights),
            RampedColor = false
            
        };

        public static LEDEffect ClassicTwinkle => new StarEffect<PaletteStar>
        {
            Blend = false,
            NewStarProbability = 3.0,
            StarPreignitonTime = 0,
            StarIgnition = 0.01,
            StarHoldTime = 0.5,
            StarFadeTime = 0.0,
            StarSize = 1,
            MaxSpeed = 0,
            ColorSpeed = 0,
            Palette = new Palette(CRGB.ChristmasLights),
            RampedColor = false
        };

        public static LEDEffect FrostyBlueStars => new StarEffect<PaletteStar>
        {
            Blend = true,
            NewStarProbability = .5,
            StarPreignitonTime = 0.05,
            StarIgnition = 0.1,
            StarHoldTime = 3.0,
            StarFadeTime = 1.0,
            StarSize = 1,
            MaxSpeed = 0,
            ColorSpeed = 1,
            Palette = new Palette(CRGB.makeGradient(new CRGB(0, 0, 64), new CRGB(0, 64, 255)))
        };

        public static LEDEffect TwinkleBlueStars => new StarEffect<PaletteStar>
        {
            Blend = true,
            NewStarProbability = 0.5,
            StarPreignitonTime = 0.1,
            StarIgnition = 0.5,
            StarHoldTime = 2.0,
            StarFadeTime = 1.0,
            StarSize = 1,
            MaxSpeed = 5,
            ColorSpeed = 1,
            Palette = new Palette(CRGB.BlueSpectrum)
        };

        public static LEDEffect SparseChristmasLights => new StarEffect<PaletteStar>
        {
            Blend = false,
            NewStarProbability = 0.20,
            StarPreignitonTime = 0.00,
            StarIgnition = 0.0,
            StarHoldTime = 5.0,
            StarFadeTime = 0.0,
            StarSize = 1,
            MaxSpeed = 2,
            ColorSpeed = .05,
            Palette = new Palette(CRGB.ChristmasLights)
        };

        public static LEDEffect SparseChristmasLights2 => new StarEffect<AlignedPaletteStar>
        {
            Blend = false,
            NewStarProbability = 0.20,
            StarPreignitonTime = 0.00,
            StarIgnition = 0.0,
            StarHoldTime = 3.0,
            StarFadeTime = 0.0,
            StarSize = 6,
            MaxSpeed = 0,
            ColorSpeed = 0,
            Alignment = 24,
            RampedColor = true,
            Palette = new Palette(CRGB.ChristmasLights)
        };

        public static LEDEffect TwinkleChristmasLights => new StarEffect<AlignedPaletteStar>
        {
            Blend = false,
            NewStarProbability = 2.0,
            StarPreignitonTime = 0.00,
            StarIgnition = 0.0,
            StarHoldTime = 0.5,
            StarFadeTime = 0.0,
            StarSize = 4,
            MaxSpeed = 0,
            ColorSpeed = 0,
            Alignment = 24,
            RampedColor = true,
            Palette = new Palette(CRGB.ChristmasLights)
        };
        public static LEDEffect ChristmasLights => new PaletteEffect(new Palette(CRGB.ChristmasLights))
        {
            _Density = 1,
            _EveryNthDot = 28,
            _DotSize = 10,
            _LEDColorPerSecond = 0,
            _LEDScrollSpeed = 10,
            _Blend = true,
            _RampedColor = true
        };
        public static LEDEffect VintageChristmasLights => new PaletteEffect(new Palette(CRGB.VintageChristmasLights))
        {
            _Density = 1,
            _EveryNthDot = 28,
            _DotSize = 10,
            _LEDColorPerSecond = 0,
            _LEDScrollSpeed = 20,
            _Blend = true,
            _RampedColor = true
        };

        public static LEDEffect FastChristmasLights => new PaletteEffect(new Palette(CRGB.ChristmasLights))
        {
            _Density = 1,
            _EveryNthDot = 28,
            _DotSize = 10,
            _LEDColorPerSecond = 0,
            _LEDScrollSpeed = 80,
            _Blend = true,
            _RampedColor = true
        };

        public static LEDEffect ChristmasLightsFast => new PaletteEffect(new Palette(CRGB.ChristmasLights))
        {
            _Density = 16,
            _EveryNthDot = 24,
            _DotSize = 1,
            _LEDColorPerSecond = 0,
            _LEDScrollSpeed = 120,
            _Blend = true,
            _RampedColor = true
        };


        public static LEDEffect LavaStars => new StarEffect<PaletteStar>
        {
            Blend = true,
            NewStarProbability = 5,
            StarPreignitonTime = 0.05,
            StarIgnition = 1.0,
            StarHoldTime = 1.0,
            StarFadeTime = 1.0,
            StarSize = 10,
            MaxSpeed = 50,
            Palette = new Palette(CRGB.HotStars)
        };

        public static LEDEffect ColorSliders => new StarEffect<PaletteStar>
        {
            Blend = true,
            NewStarProbability = .25,
            StarPreignitonTime = 0.0,
            StarIgnition = 1.0,
            StarHoldTime = 0.0,
            StarFadeTime = 1.0,
            StarSize = 1,
            MaxSpeed = 0,
            Palette = Palette.Rainbow
        };

        public static LEDEffect RainbowMiniLites => new PaletteEffect(Palette.Rainbow)
        {
            _Density = .1,
            _EveryNthDot = 14,
            _DotSize = 1,
            _LEDColorPerSecond = 50,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect RainbowStrip => new PaletteEffect(Palette.Rainbow)
        {
            _Density = .005,
            _EveryNthDot = 1,
            _DotSize = 1,
            _LEDColorPerSecond = 250,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect MiniLites => new PaletteEffect(new Palette(new CRGB[]
        {
            CRGB.Blue,
            CRGB.Cyan,
            CRGB.Green,
            CRGB.Blue,
            CRGB.Purple,
            CRGB.Pink,
            CRGB.Blue
        },
        256))
        {
            _Density = .1,
            _EveryNthDot = 14,
            _DotSize = 1,
            _LEDColorPerSecond = 50,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect RainbowColorLites => new PaletteEffect(Palette.Rainbow)
        {
            _Density = 0.15,
            _EveryNthDot = 8,
            _DotSize = 3,
            _LEDColorPerSecond = 100,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect CupboardRainbowSweep => new PaletteEffect(Palette.Rainbow)
        {
            _Density = .5 / 16,
            _EveryNthDot = 10,
            _DotSize = 10,
            _LEDColorPerSecond = 50,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect ColorFadeMiniLites => new PaletteEffect(Palette.Rainbow)
        {
            _Density = 0,
            _EveryNthDot = 14,
            _DotSize = 1,
            _LEDColorPerSecond = 3,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect RidersEffect => new PaletteEffect(new Palette(CRGB.Football_Regina))
        {
            _Density = 1,
            _EveryNthDot = 2,
            _DotSize = 1,
            _LEDColorPerSecond = 30,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect RidersEffect2 => new PaletteEffect(new Palette(CRGB.Football_Regina2))
        {
            _Density = 1,
            _EveryNthDot = 2,
            _DotSize = 1,
            _LEDColorPerSecond = 30,
            _LEDScrollSpeed = 0,
            _Blend = false      
        };

        public static LEDEffect Football_Effect_Seattle => new PaletteEffect(new Palette(CRGB.Football_Seattle, CRGB.Football_Seattle.Length))
        {
            _Density = 1,
            _EveryNthDot = 10,
            _DotSize = 7,
            _LEDColorPerSecond = 0,
            _LEDScrollSpeed = 20,
            _Blend = true
        };

        public static LEDEffect Football_Effect_Seattle2 => new PaletteEffect(new Palette(CRGB.Football_Seattle, CRGB.Football_Seattle.Length))
        {
            _Density = 8,
            _EveryNthDot = 10,
            _DotSize = 5,
            _LEDColorPerSecond = 0,
            _LEDScrollSpeed = 20,
            _Blend = true
        };

        public static LEDEffect BluePulse => new PaletteFillEffect(new Palette(CRGB.BluePeak))
        {
            EveryNthDot = 1,
            ColorPerSecond = 250,
            ScrollSpeed = 0
        };

        public static LEDEffect NeonSpears => new PaletteEffect(new Palette(CRGB.RainbowStripes, 1024))
        {
            _Density = 0.001,
            _EveryNthDot = 1,
            _DotSize = 1,
            _LEDColorPerSecond = 100,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect Spears2 => new PaletteEffect(new Palette(CRGB.Rainbow, 1024))
        {
            _Density = 0.5,
            _EveryNthDot = 1,
            _DotSize = 1,
            _LEDColorPerSecond = 200,
            _LEDScrollSpeed = 0,
            _Blend = true
        };

        public static LEDEffect C9 => new PaletteEffect(new Palette(CRGB.VintageChristmasLights, 16))
        {
            _Density = 1,
            _EveryNthDot = 1,
            _DotSize = 1,
            _LEDColorPerSecond = 4,
            _LEDScrollSpeed = 0,
            _Blend = false
        };

        public static LEDEffect SeawawksTwinkleStarEffect => new StarEffect<PaletteStar>
        {
            Palette = new Palette(CRGB.Football_Seattle),
            Blend = true,
            NewStarProbability = 3,
            StarPreignitonTime = 0.05,
            StarIgnition = .5,
            StarHoldTime = 1.0,
            StarFadeTime = .5,
            StarSize = 2,
            MaxSpeed = 0,
            ColorSpeed = 0,
            RandomStartColor = false,
            BaseColorSpeed = 0.25,
            RandomStarColorSpeed = false
        };

        public static LEDEffect RainbowTwinkleStarEffect => new StarEffect<PaletteStar>
        {
            Palette = Palette.Rainbow,
            Blend = true,
            NewStarProbability = 3,
            StarPreignitonTime = 0.05,
            StarIgnition = .5,
            StarHoldTime = .0,
            StarFadeTime = 1.5,
            StarSize = 1,
            MaxSpeed = 0,
            ColorSpeed = 10,
            RandomStartColor = false,
            BaseColorSpeed = 0.005,
            RandomStarColorSpeed = false
        };

        public static LEDEffect BasicColorTwinkleStarEffect
        {
            get
            {
                return new StarEffect<ColorStar>
                {
                    Blend = true,
                    NewStarProbability = 5,
                    StarPreignitonTime = 0.05,
                    StarIgnition = .5,
                    StarHoldTime = 1.0,
                    StarFadeTime = .5,
                    StarSize = 1,
                    MaxSpeed = 2,
                    ColorSpeed = -2,
                    RandomStartColor = false,
                    BaseColorSpeed = 0.2,
                    RandomStarColorSpeed = false
                };
            }
        }

        public static LEDEffect SubtleColorTwinkleStarEffect
        {
            get
            {
                return new StarEffect<ColorStar>
                {
                    Blend = true,
                    NewStarProbability = 5,
                    StarPreignitonTime = 0.5,
                    StarIgnition = 0,
                    StarHoldTime = 1.0,
                    StarFadeTime = .5,
                    StarSize = 1,
                    MaxSpeed = 2,
                    ColorSpeed = -2,
                    RandomStartColor = false,
                    BaseColorSpeed = 0.2,
                    RandomStarColorSpeed = false
                };
            }
        }

        public static LEDEffect ToyFireTruck
        {
            get
            {
                return new StarEffect<PaletteStar>
                {
                    Palette = new Palette(new CRGB[] { CRGB.Red, CRGB.Red }),
                    Blend = true,
                    NewStarProbability = 25,
                    StarPreignitonTime = 0.05,
                    StarIgnition = .5,
                    StarHoldTime = 0.0,
                    StarFadeTime = .5,
                    StarSize = 1,
                    MaxSpeed = 5,
                    ColorSpeed = -2,
                    RandomStartColor = false,
                    BaseColorSpeed = 50,
                    RandomStarColorSpeed = false,
                    Direction = -1
                };
            }
        }

        public static LEDEffect CharlieBrownTree
        {
            get
            {
                return new StarEffect<ColorStar>
                {
                    Blend = true,
                    NewStarProbability = 5, // 25,
                    StarPreignitonTime = 0.05,
                    StarIgnition = .5,
                    StarHoldTime = 0.0,
                    StarFadeTime = .5,
                    StarSize = 1,
                    MaxSpeed = 20,
                    ColorSpeed = -2,
                    RandomStartColor = false,
                    BaseColorSpeed = 5,
                    RandomStarColorSpeed = false,
                    Direction = 1
                };
            }
        }

        public static LEDEffect Mirror
        {
            get
            {
                return new StarEffect<ColorStar>
                {
                    Blend = true,
                    NewStarProbability = 25,
                    StarPreignitonTime = 0.05,
                    StarIgnition = .5,
                    StarHoldTime = 0.0,
                    StarFadeTime = .5,
                    StarSize = 1,
                    MaxSpeed = 50,
                    ColorSpeed = -2,
                    RandomStartColor = false,
                    BaseColorSpeed = 5,
                    RandomStarColorSpeed = false,
                    Direction = 1
                };
            }
        }

        public static LEDEffect FastColorSpokes => new PaletteEffect(new Palette(CRGB.Rainbow, 1024))
        {
            _Density = 2,
            _EveryNthDot = 12,
            _DotSize = 3,
            _LEDColorPerSecond = 0,
            _LEDScrollSpeed = 40,
            _Blend = true

        };

        public static LEDEffect ColorTunnel => new PaletteEffect(new Palette(CRGB.Rainbow, 1024))
        {
            _Density = .36,
            _EveryNthDot = 3,
            _DotSize = 1,
            _LEDColorPerSecond = 250,
            _LEDScrollSpeed = 5,
            _Blend = true
        };

        public static LEDEffect SlowNeonRails => new PaletteEffect(new Palette(CRGB.Rainbow, 1024))
        {
            _Density = 1/2.0,
            _LEDColorPerSecond = 750,
            _LEDScrollSpeed = 1,
        };

        public static LEDEffect Mirror3
        {
            get
            {
                return new StarEffect<ColorStar>
                {
                    Blend = true,
                    NewStarProbability = 15, // 25,
                    StarPreignitonTime = 0.05,
                    StarIgnition = .0,
                    StarHoldTime = 0.0,
                    StarFadeTime = .5,
                    StarSize = 1,
                    MaxSpeed = 0,
                    ColorSpeed = 0,
                    RandomStartColor = false,
                    BaseColorSpeed = 10,
                    RandomStarColorSpeed = false,
                };
            }
        }
    }

    // Cabana
    //
    // Location definitio for the lights on the eaves of the Cabana

    public class Cabana : Location
    {
        const bool compressData = true;
        const int CABANA_START = 0;
        const int CABANA_1 = CABANA_START;
        const int CABANA_1_LENGTH = (5 * 144 - 1) + (3 * 144);
        const int CABANA_2 = CABANA_START + CABANA_1_LENGTH;
        const int CABANA_2_LENGTH = 5 * 144 + 55;
        const int CABANA_3 = CABANA_START + CABANA_2_LENGTH + CABANA_1_LENGTH;
        const int CABANA_3_LENGTH = 6 * 144 + 62;
        const int CABANA_4 = CABANA_START + CABANA_3_LENGTH + CABANA_2_LENGTH + CABANA_1_LENGTH;
        const int CABANA_4_LENGTH = 8 * 144 - 23;
        const int CABANA_LENGTH = CABANA_1_LENGTH + CABANA_2_LENGTH + CABANA_3_LENGTH + CABANA_4_LENGTH;

        private CRGB[] _LEDs = InitializePixels<CRGB>(CABANA_LENGTH);

        // 210 Error
        // 208 Accepts a few then resets
        // 136 Accepts a few then resets

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.1.38", "CBWEST1", compressData, CABANA_1_LENGTH, 1, CABANA_1, false) {  },
            new LightStrip("192.168.1.42", "CBEAST1", compressData, CABANA_2_LENGTH, 1, CABANA_2, true)  {  },
            new LightStrip("192.168.1.39", "CBEAST2", compressData, CABANA_3_LENGTH, 1, CABANA_3, false) {  },
            new LightStrip("192.168.1.41", "CBEAST3", compressData, CABANA_4_LENGTH, 1, CABANA_4, false) {  },
        };

        public ScheduledEffect[] _LEDEffects =
        {                 
            // Dark parts of day
            //new ScheduledEffect(ScheduledEffect.AllDays,  0,  24, new TimeOfNightEffect()),
            
            new ScheduledEffect(ScheduledEffect.AllDays,  0,  1, new SimpleColorFillEffect(CRGB.RandomSaturatedColor.fadeToBlackBy(0.90f), 4)),
            new ScheduledEffect(ScheduledEffect.AllDays,  1,  2, new SimpleColorFillEffect(CRGB.RandomSaturatedColor.fadeToBlackBy(0.90f), 4)),
            new ScheduledEffect(ScheduledEffect.AllDays,  2,  3, new SimpleColorFillEffect(CRGB.RandomSaturatedColor.fadeToBlackBy(0.90f), 4)),
            new ScheduledEffect(ScheduledEffect.AllDays,  3,  4, new SimpleColorFillEffect(CRGB.RandomSaturatedColor.fadeToBlackBy(0.90f), 4)),
            new ScheduledEffect(ScheduledEffect.AllDays,  4,  5, new SimpleColorFillEffect(CRGB.RandomSaturatedColor.fadeToBlackBy(0.75f), 2)),
            new ScheduledEffect(ScheduledEffect.AllDays,  6,  7, new SimpleColorFillEffect(CRGB.RandomSaturatedColor.fadeToBlackBy(0.50f), 2)),
            new ScheduledEffect(ScheduledEffect.AllDays,  8,  9, EffectsDatabase.QuietBlueStars),


            new ScheduledEffect(ScheduledEffect.AllDays,  9, 21, new FireworksEffect() { NewParticleProbability = 2.0 } ),

            new ScheduledEffect(ScheduledEffect.AllDays,  5, 21, EffectsDatabase.RainbowStrip),
            new ScheduledEffect(ScheduledEffect.AllDays,  9, 21, EffectsDatabase.BasicColorTwinkleStarEffect),
           
            new ScheduledEffect(ScheduledEffect.AllDays,  5, 21, EffectsDatabase.ColorFadeMiniLites),
            new ScheduledEffect(ScheduledEffect.AllDays,  5, 21, EffectsDatabase.RainbowMiniLites),
            new ScheduledEffect(ScheduledEffect.AllDays,  21, 24, EffectsDatabase.QuietBlueStars),

            
            };

        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    };

    // Bench
    //
    // Location definition for the test rig on the workbench

    public class Bench : Location
    {
        const bool compressData = true;
        const int BENCH_START   = 0;
        const int BENCH_LENGTH  = 144*3;

        private CRGB[] _LEDs    = InitializePixels<CRGB>(BENCH_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.1.51", "BENCH", compressData, BENCH_LENGTH, 1, BENCH_START, false) {  }  // 216
        };

        public ScheduledEffect[] _LEDEffects =
        {
                    
            //new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, EffectsDatabase.Football_Effect_Seattle2) 
            //new ScheduledEffect(ScheduledEffect.AllDays,  0, 24,  EffectsDatabase.QuietBlueStars),
            //new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, new FireEffect(3*144, true) { _Cooling = 1000 } ),
            //new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, new FireworksEffect() { NewParticleProbability = 2.0 } )
            new ScheduledEffect(ScheduledEffect.AllDays,  0, 24,  EffectsDatabase.QuietBlueStars),
            /*
            new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, new PaletteEffect(Palette.Rainbow2)
            {
                _Density = .15,
                _EveryNthDot = 1,
                _DotSize = 1,
                _LEDColorPerSecond = 150,
                _LEDScrollSpeed = 0,
                _Brightness = .25,
                _Blend = true
            }),
            */
        };

        public override LightStrip[] LightStrips   { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects    { get { return _LEDEffects; } }
        protected override CRGB[] LEDs                  { get { return _LEDs; } }
    };

    public class Tree : Location
    {
        const bool compressData = true;
        const int TREE_START = 0;
        const int TREE_LENGTH = 144;

        private CRGB[] _LEDs = InitializePixels<CRGB>(TREE_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.1.52", "TREE", compressData, TREE_LENGTH, 1, TREE_START, false, 0, false),
        };

        public ScheduledEffect[] _LEDEffects =
        {
            //new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, new FireworksEffect() { NewParticleProbability = 3.0 } )
            //new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, EffectsDatabase.CharlieBrownTree),
            new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new PaletteEffect(Palette.Rainbow)
            {
                _Density = .5,
                _EveryNthDot = 10,
                _DotSize = 2,
                _LEDColorPerSecond = 0,
                _LEDScrollSpeed = 5,
                _Blend = true,
                _Brightness = .25
            })
        };

        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    };

    public class Mirror : Location
    {
        const bool compressData = true;
        const int BENCH_START = 0;
        const int BENCH_LENGTH = 93;

        private CRGB[] _LEDs = InitializePixels<CRGB>(BENCH_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.1.53", "MIRROR", compressData, BENCH_LENGTH, 1, BENCH_START, false, 0, false),
        };

        public ScheduledEffect[] _LEDEffects =
        {
            new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, EffectsDatabase.Football_Effect_Seattle2),
            new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, EffectsDatabase.Mirror),
            new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, EffectsDatabase.Mirror3),
            new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, EffectsDatabase.ColorTunnel),
        };

        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    };
    /*
        public class Truck : Location
        {
            const bool compressData = true;
            const int TRUCK_START = 0;
            const int TRUCK_LENGTH = 270;

            private CRGB[] _LEDs = InitializePixels<CRGB>(TRUCK_LENGTH);

            private LightStrip[] _StripControllers =
            {
                new LightStrip("192.168.0.215", "TREE", compressData, TRUCK_LENGTH, 1, TRUCK_START, false),
            };

            public ScheduledEffect[] _LEDEffects =
            {
                new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, EffectsDatabase.C9)
            };

            public override LightStrip[] LightStrips { get { return _StripControllers; } }
            public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
            protected override CRGB[] LEDs { get { return _LEDs; } }
        };
    */
    public class AtomLight : Location
    {
        const bool compressData = true;
        const int ATOM_START = 0;
        const int ATOM_LENGTH = 53;

        private CRGB[] _LEDs = InitializePixels<CRGB>(ATOM_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.0.148", "ATOM0", compressData, ATOM_LENGTH, 1, ATOM_START, false, 1 + 2 + 4 + 8),
            //new LightStrip("192.168.0.251", "ATOM1", compressData, ATOM_LENGTH, 1, ATOM_START, false, 2),
            //new LightStrip("192.168.0.251", "ATOM2", compressData, ATOM_LENGTH, 1, ATOM_START, false, 4),
            //new LightStrip("192.168.0.251", "ATOM3", compressData, ATOM_LENGTH, 1, ATOM_START, false, 8),
        };

        public ScheduledEffect[] _LEDEffects =
        {
            new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, EffectsDatabase.Football_Effect_Seattle),
        };

        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    };

    public class TV : Location
    {
        const bool compressData = true;
        const int TV_START = 0;
        const int TV_LENGTH = 144 * 5;

        private CRGB[] _LEDs = InitializePixels<CRGB>(TV_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.0.136", "TV",        compressData, TV_LENGTH,         1, TV_START, false) { }
        };

        public ScheduledEffect[] _LEDEffects =
        {
            //new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new RainbowEffect(0, 20)),
            //new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new RainbowEffect(1, 100)),
            //new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new FireEffect(4 * 144, true) { _Cooling = 350 } )
            /*
            new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new PaletteEffect(new Palette( CRGB.Reds, CRGB.Reds.Length))
            {
                _Density = .1,
                _EveryNthDot = 10,
                _DotSize = 10,
                _LEDColorPerSecond = 0,
                _LEDScrollSpeed = 10,
                _Blend = true
            })
            */
            new ScheduledEffect(ScheduledEffect.AllDays,  0, 24, new SimpleColorFillEffect(CRGB.Red))
    };

        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    }

    // ShopCupboards
    //
    // Location definition for the up-lights on top of the shop cupboards

    public class ShopCupboards : Location
    {
        const bool compressData = true;

        const int CUPBOARD_START = 0;
        const int CUPBOARD_1_START = CUPBOARD_START;
        const int CUPBOARD_1_LENGTH = 300 + 200;
        const int CUPBOARD_2_START = CUPBOARD_1_START + CUPBOARD_1_LENGTH;
        const int CUPBOARD_2_LENGTH = 300 + 300;                                   // 90 cut from one 
        const int CUPBOARD_3_START = CUPBOARD_2_START + CUPBOARD_2_LENGTH;
        const int CUPBOARD_3_LENGTH = 144;
        const int CUPBOARD_4_START = CUPBOARD_2_START + CUPBOARD_2_LENGTH + CUPBOARD_3_LENGTH;
        const int CUPBOARD_4_LENGTH = 82;
        const int CUPBOARD_LENGTH = CUPBOARD_1_LENGTH + CUPBOARD_2_LENGTH + CUPBOARD_3_LENGTH + CUPBOARD_4_LENGTH;

        private CRGB[] _LEDs = InitializePixels<CRGB>(CUPBOARD_LENGTH);

        private LightStrip[] _StripControllers =
        {

            new LightStrip("192.168.1.37", "CUPBOARD1", compressData, CUPBOARD_1_LENGTH, 1, CUPBOARD_1_START, false),
            new LightStrip("192.168.1.47", "CUPBOARD2", compressData, CUPBOARD_2_LENGTH, 1, CUPBOARD_2_START, false),
            new LightStrip("192.168.1.49", "CUPBOARD3", compressData, CUPBOARD_3_LENGTH, 1, CUPBOARD_3_START, false),  // WHOOPS
            new LightStrip("192.168.1.46", "CUPBOARD4", compressData, CUPBOARD_4_LENGTH, 1, CUPBOARD_4_START, false),
        };

        public ScheduledEffect[] _LEDEffects =
        {
            //new ScheduledEffect(ScheduledEffect.AllDays,  5, 21, new SimpleColorFillEffect(CRGB.Blue)),
            //new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new SimpleColorFillEffect(CRGB.Orange, 1))
            //new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new SimpleColorFillEffect(new CRGB(64, 255, 128), 1))
            new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new PaletteEffect(Palette.Rainbow) { _EveryNthDot = 1, _DotSize = 1, _Density = 0.075/32, _LEDColorPerSecond = 28 }),
        };

        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    };

    // ShopSouthWindows
    //
    // Location definition for the lights int the 3-window south shop bay window


    public class ShopSouthWindows1 : Location
    {
        const bool compressData = true;

        const int WINDOW_START = 0;
        const int WINDOW_1_START = 0;
        const int WINDOW_1_LENGTH = 5 * 144;

        const int WINDOW_LENGTH = WINDOW_1_LENGTH;

        private CRGB[] _LEDs = InitializePixels<CRGB>(WINDOW_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.1.34", "WINDOW1", compressData, WINDOW_1_LENGTH, 1, WINDOW_1_START, false),
        };

        public ScheduledEffect[] _LEDEffects =
        {
            new ScheduledEffect(ScheduledEffect.AllDays, 0, 24,
                new SimpleColorFillEffect(CRGB.Green.fadeToBlackBy(0.2), 1))
        };


        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    }

    public class ShopSouthWindows2 : Location
    {
        const bool compressData = true;

        const int WINDOW_START = 0;
        const int WINDOW_1_START = 0;
        const int WINDOW_1_LENGTH = 5 * 144;

        const int WINDOW_LENGTH = WINDOW_1_LENGTH;

        private CRGB[] _LEDs = InitializePixels<CRGB>(WINDOW_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.1.35", "WINDOW2", compressData, WINDOW_1_LENGTH, 1, WINDOW_1_START, false),
        };

        public ScheduledEffect[] _LEDEffects =
        {
            new ScheduledEffect(ScheduledEffect.AllDays, 0, 24,
                new SimpleColorFillEffect(CRGB.Blue.blendWith(CRGB.Cyan).fadeToBlackBy(0.5), 1))
        };


        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    }

    public class ShopSouthWindows3 : Location
    {
        const bool compressData = true;

        const int WINDOW_START = 0;
        const int WINDOW_1_START = 0;
        const int WINDOW_1_LENGTH = 5 * 144;

        const int WINDOW_LENGTH = WINDOW_1_LENGTH;

        private CRGB[] _LEDs = InitializePixels<CRGB>(WINDOW_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.1.36", "WINDOW3", compressData, WINDOW_1_LENGTH, 1, WINDOW_1_START, false),
        };

    public ScheduledEffect[] _LEDEffects =
    {
            new ScheduledEffect(ScheduledEffect.AllDays, 0, 24,
                new SimpleColorFillEffect(CRGB.Red.blendWith(CRGB.Orange, .75).fadeToBlackBy(0.5), 1))
        };


        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    }

    /*
    public class ShopSouthWindows : Location
    {
        const bool compressData = true;

        const int WINDOW_START = 0;
        const int WINDOW_1_START = 0;
        const int WINDOW_1_LENGTH = 5 * 144;
        const int WINDOW_2_START = WINDOW_1_START + WINDOW_1_LENGTH;
        const int WINDOW_2_LENGTH = 5 * 144;                                   // 90 cut from one 
        const int WINDOW_3_START = WINDOW_2_START + WINDOW_2_LENGTH;
        const int WINDOW_3_LENGTH = 5 * 144;

        const int WINDOW_LENGTH = WINDOW_1_LENGTH + WINDOW_2_LENGTH + WINDOW_3_LENGTH;

        private CRGB[] _LEDs = InitializePixels<CRGB>(WINDOW_LENGTH);

        private LightStrip[] _StripControllers =
        {
            new LightStrip("192.168.1.34", "WINDOW1", compressData, WINDOW_1_LENGTH, 1, WINDOW_1_START, false),
            new LightStrip("192.168.1.35", "WINDOW2", compressData, WINDOW_2_LENGTH, 1, WINDOW_2_START, false),
            new LightStrip("192.168.1.36", "WINDOW3", compressData, WINDOW_3_LENGTH, 1, WINDOW_3_START, false),

        };

        public ScheduledEffect[] _LEDEffects =
        {
            new ScheduledEffect(ScheduledEffect.AllDays, 0, 24,
                new PaletteEffect(Palette.Rainbow)
                {
                    _Density = 0.025, _LEDScrollSpeed = 35, _LEDColorPerSecond = 0, _DotSize = 1, _EveryNthDot = 1, _Brightness = 0.75
                }
            ),
        };


        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    };
    */

    // ShopEastWindows
    //
    // Location definition for the lights int the 3-window south shop bay window


    public class ShopEastWindows : Location
    {
        const bool compressData = true;

        const int WINDOW_START = 0;
        const int WINDOW_2_START = 0;
        const int WINDOW_2_LENGTH = 7 * 144;

        const int WINDOW_LENGTH = WINDOW_2_LENGTH;

        private CRGB[] _LEDs = InitializePixels<CRGB>(WINDOW_LENGTH);

        private LightStrip[] _StripControllers =
        {           // 192.168.1.18
            new LightStrip("192.168.1.48", "WINDOWEAST", compressData, WINDOW_2_LENGTH, 1, WINDOW_2_START, false),
        };
        private static readonly ScheduledEffect[] _LEDEffects =
        {
            //new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, EffectsDatabase.ChristmasLights),
            //new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new SimpleColorFillEffect(CRGB.GetBlackbodyHeatColor(0.80).fadeToBlackBy(0.75), 1))
            //new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new SimpleColorFillEffect(CRGB.Green, 2)),
            new ScheduledEffect(ScheduledEffect.AllDays, 0, 24, new PaletteEffect(Palette.Rainbow)
                {
                    _Density = 0.0025, _LEDScrollSpeed = 100, _LEDColorPerSecond = 250, _DotSize = 1, _EveryNthDot = 1, _Brightness = 0.5
                })
            //new ScheduledEffect(ScheduledEffect.AllDays, 17, 21, new FireEffect(WINDOW_2_LENGTH, true)),
        };

        public override LightStrip[] LightStrips { get { return _StripControllers; } }
        public override ScheduledEffect[] LEDEffects { get { return _LEDEffects; } }
        protected override CRGB[] LEDs { get { return _LEDs; } }
    };
    
}