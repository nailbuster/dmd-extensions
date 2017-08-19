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
	public abstract class Animation
	{
		public string Name { get; protected set; }

		/// <summary>
		/// Number of frames contained in this animation
		/// </summary>
		public int NumFrames => Frames.Count;

		public AnimationStatus Status { get; }

		/// <summary>
		/// Number of bitplanes the frames of the animation are made of when
		/// replaying replacement frames
		/// </summary>
		public int NumPlanes => Frames.Count > 0 ? Frames[0].Planes.Count : 0;

		/// <summary>
		/// Byte position of this animation in the file.
		/// </summary>
		/// 
		/// <remarks>
		/// Used as index to load animations.
		/// </remarks>
		public readonly long Offset;

		/// <summary>
		/// Next hash to look for (in col seq mode)
		/// </summary>
		uint Crc32 { get; }

		/// <summary>
		/// Mask for "Follow" switch mode.
		/// </summary>
		byte[] Mask { get; }

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

		protected int PaletteIndex;
		protected Color[] AnimationColors;
		protected AnimationEditMode EditMode;
		protected int TransitionFrom;

		protected int Width;
		protected int Height;

		protected List<AnimationFrame> Frames;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		protected Animation(long offset)
		{
			Offset = offset;
			Status = new AnimationStatus(this);
		}

		/// <summary>
		/// Resets stops the animation and resets the status.
		/// </summary>
		public void Reset()
		{
			
		}

		public override string ToString()
		{
			return $"{Name}, {Frames.Count} frames";
		}
	}

	public class AnimationStatus
	{

		/// <summary>
		/// Number of remaining frames until the animation ends
		/// </summary>
		public int RemainingFrames => _parent.NumFrames - _frameIndex;

		/// <summary>
		/// If true then bitplanes are added instead of being replaced
		/// </summary>
		public bool AddPlanes => SwitchMode == SwitchMode.Follow || SwitchMode == SwitchMode.ColorMask;

		/// <summary>
		/// Switch mode used for this animation
		/// </summary>
		public SwitchMode SwitchMode { get; }

		private readonly Animation _parent;
		private int _frameIndex;

		public AnimationStatus(Animation parent)
		{
			_parent = parent;
		}

		public void NextFrame()
		{
			_frameIndex++;
		}
	}

	public enum AnimationEditMode
	{
		Replace, Mask, Fixed
	}
}
