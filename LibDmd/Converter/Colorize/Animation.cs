using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
		public int NumFrames => Frames.Length;

		/// <summary>
		/// Number of bitplanes the frames of the animation are made of when
		/// replaying replacement frames
		/// </summary>
		public int NumPlanes => Frames.Length > 0 ? Frames[0].Planes.Count : 0;

		/// <summary>
		/// Byte position of this animation in the file.
		/// </summary>
		/// 
		/// <remarks>
		/// Used as index to load animations.
		/// </remarks>
		public readonly long Offset;

		public AnimationStatus Status { get; private set; }

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

		protected AnimationFrame[] Frames;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private IObservable<AnimationFrame> _frames;
		private byte[][] _lastVpmFrame;
		private IDisposable _animation;

		protected Animation(long offset)
		{
			Offset = offset;
		}
		
		public void Start(SwitchMode mode, byte[][] firstFrame, Subject<Tuple<byte[][], Color[]>> coloredGray2Source, Subject<Tuple<byte[][], Color[]>> coloredGray4Source, Palette palette, Action completed = null)
		{
			Status = new AnimationStatus(this, mode);

			_frames = Frames.ToObservable().Delay(frame => Observable.Timer(TimeSpan.FromMilliseconds(frame.Time)));
			if (Status.AddPlanes) {
				StartEnhance(firstFrame, coloredGray4Source, palette, completed);

			} else {
				StartReplace(coloredGray2Source, coloredGray4Source, palette, completed);
			}
		}

		public void NextFrame(byte[][] planes)
		{
			_lastVpmFrame = planes;
		}

		/// <summary>
		/// Tuät d Animazion looslah und d Biudli uifd viärbit-Queuä uisgäh.
		/// </summary>
		/// <remarks>
		/// Das hiä isch dr Fau wo Buider vo VPM mit zwe Bits erwiiterid wärdid.
		/// 
		/// S Timing wird wiä im Modus eis vo dr Animazion vorgäh, das heisst s 
		/// letschtä Biud vo VPM definiärt diä erschtä zwäi Bits unds jedes Biud
		/// vord Animazion tuät diä reschtlichä zwäi Bits ergänzä unds de uifd
		/// Viärbit-Queuä uisgäh.
		/// </remarks>
		/// <param name="firstFrame">S Buid vo VPM wod Animazion losgla het</param>
		/// <param name="coloredGray4Source">D Uisgab vord erwiitertä Frames</param>
		/// <param name="palette">D Palettä wo zum iifärbä bruicht wird</param>
		/// <param name="completed">Wird uisgfiärt wenn fertig</param>
		private void StartEnhance(byte[][] firstFrame, Subject<Tuple<byte[][], Color[]>> coloredGray4Source, Palette palette, Action completed = null)
		{
			Logger.Info("[vni] Starting enhanced animation of {0} frames...", Frames.Length);
			var t = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
			var n = 0;
			_lastVpmFrame = firstFrame;
			_animation = _frames
				.Select(frame => new []{ _lastVpmFrame[0], _lastVpmFrame[1], frame.Planes[0].Plane, frame.Planes[1].Plane })
				.Subscribe(planes => {
					//Logger.Trace("[timing] FSQ enhanced Frame #{0} played ({1} ms, theory: {2} ms).", n, (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) - t, _frames[n].Time);
					coloredGray4Source.OnNext(new Tuple<byte[][], Color[]>(planes, palette.GetColors(planes.Length)));
					n++;
				}, () => {
					//Logger.Trace("[timing] Last frame enhanced, waiting {0}ms for last frame to finish playing.", _frames[_frames.Length - 1].Delay);

					// nu uifs letschti biud wartä bis mer fertig sind
					Observable
						.Never<Unit>()
						.StartWith(Unit.Default)
						.Delay(TimeSpan.FromMilliseconds(Frames[Frames.Count() - 1].Delay))
						.Subscribe(_ => {
							Reset();
							completed?.Invoke();
						});
				});
		}

		/// <summary>
		/// Tuät d Animazion looslah und d Biudli uif diä entschprächendi Queuä
		/// uisgäh.
		/// </summary>
		/// <remarks>
		/// Das hiä isch dr Fau wo diä gsamti Animazion uisgäh und VPM ignoriärt
		/// wird (dr Modus eis).
		/// </remarks>
		/// <param name="coloredGray2Source">Wenn meglich gahts da druif</param>
		/// <param name="coloredGray4Source">Wenns viärbittig isch, de wird dä zersch probiärt</param>
		/// <param name="palette">D Palettä wo zum iifärbä bruicht wird</param>
		/// <param name="completed">Wird uisgfiärt wenn fertig</param>
		private void StartReplace(Subject<Tuple<byte[][], Color[]>> coloredGray2Source, Subject<Tuple<byte[][], Color[]>> coloredGray4Source, Palette palette, Action completed = null)
		{
			Logger.Info("[vni] Starting colored gray4 animation of {0} frames...", Frames.Length);
			var t = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
			var n = 0;
			_animation = _frames
				.Subscribe(frame => {
					//Logger.Trace("[timing] FSQ Frame #{0} played ({1} ms, theory: {2} ms).", n, (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) - t, _frames[n].Time);
					if (frame.BitLength == 2) {
						coloredGray2Source.OnNext(new Tuple<byte[][], Color[]>(frame.PlaneData, palette.GetColors(frame.BitLength)));
					} else {
						coloredGray4Source.OnNext(new Tuple<byte[][], Color[]>(frame.PlaneData, palette.GetColors(frame.BitLength)));
					}
					n++;
				}, () => {

					// nu uifs letschti biud wartä bis mer fertig sind
					Observable
						.Never<Unit>()
						.StartWith(Unit.Default)
						.Delay(TimeSpan.FromMilliseconds(Frames[Frames.Length - 1].Delay))
						.Subscribe(_ => {
							completed?.Invoke();
						});
				});
		}

		/// <summary>
		/// Resets stops the animation and resets the status.
		/// </summary>
		public void Reset()
		{
			Status = null;
		}

		public override string ToString()
		{
			return $"{Name}, {Frames.Length} frames";
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

		public AnimationStatus(Animation parent, SwitchMode switchMode)
		{
			_parent = parent;
			SwitchMode = switchMode;
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
