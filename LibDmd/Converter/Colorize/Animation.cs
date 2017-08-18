using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using LibDmd.Common;
using LibDmd.Common.HeatShrink;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public abstract class AnimationSet
	{
		public int Version { get; protected set; }
		public List<Animation> Animations { get; protected set; }

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public override string ToString()
		{
			return $"VPIN v{Version}, {Animations.Count} animation(s)";
		}
	}


	public abstract class Animation
	{
		public string Name { get; protected set; }
		protected int Cycles;
		protected int Hold;
		protected int ClockFrom;
		protected bool ClockSmall;
		protected bool ClockInFront;
		protected int ClockOffsetX;
		protected int ClockOffsetY;
		protected int RefreshDelay;
		[Obsolete] protected int Type;
		protected int Fsk;

		public int PaletteIndex { get; protected set; }
		public Color[] AnimationColors { get; protected set; }
		public AnimationEditMode EditMode { get; protected set; }
		public int TransitionFrom { get; protected set; }

		public int Width { get; protected set; }
		public int Height { get; protected set; }

		public List<AnimationFrame> Frames;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public override string ToString()
		{
			return $"{Name}, {Frames.Count} frames";
		}
	}

	public enum AnimationEditMode
	{
		Replace, Mask, Fixed
	}

	public abstract class AnimationFrame
	{
		/// <summary>
		/// Duration of the frame
		/// </summary>
		public int Delay { get; protected set; }
		public List<AnimationPlane> Planes { get; protected set; }
		public bool HasMask { get; protected set; }
		public byte[] Hash { get; protected set; }

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	}

	public abstract class AnimationPlane
	{
		/// <summary>
		/// Type of plane
		/// </summary>
		public byte Marker { get; protected set; }
		public byte[] Plane { get; protected set; }
	}
}
