using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Media;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public abstract class Animation
	{
		public string Name { get; protected set; }

		/// <summary>
		/// Wefu Biudr diä Animazion het
		/// </summary>
		public int NumFrames => Frames.Length;

		/// <summary>
		/// Bitlängi odr Ahzahl Planes vo dr Buidr vo dr Animazion
		/// </summary>
		public int BitLength => Frames.Length > 0 ? Frames[0].BitLength : 0;

		/// <summary>
		/// Uif welärä Posizion i Bytes d Animazion im Feil gsi isch
		/// </summary>
		/// 
		/// <remarks>
		/// Wird aus Index zum Ladä bruicht.
		/// </remarks>
		public readonly long Offset;

		/// <summary>
		/// D Biudr vo dr Animazion
		/// </summary>
		protected AnimationFrame[] Frames;

		#region Unused Props
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
		#endregion

		#region Animation-related

		/// <summary>
		/// Wiä langs nu gaht bisd Animazion fertig isch
		/// </summary>
		public int RemainingFrames => NumFrames - _frameIndex;

		/// <summary>
		/// Faus ja de wärdid Biudr ergänzt, sisch wärdits uistuischt
		/// </summary>
		public bool AddPlanes => SwitchMode == SwitchMode.Follow || SwitchMode == SwitchMode.ColorMask;

		/// <summary>
		/// Dr Modus vo dr Animazion wo bestimmt wiäd Biudr aagwänded wärdid
		/// </summary>
		public SwitchMode SwitchMode { get; private set; }

		/// <summary>
		/// Zeigt ah obd Animazion nu am laifä isch
		/// </summary>
		public bool IsRunning { get; private set; }


		private IObservable<AnimationFrame> _frames;
		private byte[][] _currentVpmFrame;
		private IDisposable _animation;
		private int _frameIndex;

		#endregion

		/// <summary>
		/// Next hash to look for (in col seq mode)
		/// </summary>
		uint Crc32 { get; }

		/// <summary>
		/// Mask for "Follow" switch mode.
		/// </summary>
		byte[] Mask { get; }

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		protected Animation(long offset)
		{
			Offset = offset;
		}
		
		/// <summary>
		/// Tuät d Animazion startä.
		/// </summary>
		/// 
		/// <param name="mode">Dr Modus i welem d Animazion laift (chunnt uifs Mappind druif ah)</param>
		/// <param name="firstFrame">S Buid vo VPM wod Animazion losgla het</param>
		/// <param name="render">Ä Funktion wo tuät s Buid uisgäh</param>
		/// <param name="completed">Wird uisgfiärt wenn fertig</param>
		public void Start(SwitchMode mode, byte[][] firstFrame, Action<byte[][]> render, Action completed = null)
		{
			IsRunning = true;
			SwitchMode = mode;
			_frames = Frames.ToObservable().Delay(frame => Observable.Timer(TimeSpan.FromMilliseconds(frame.Time)));
			if (AddPlanes) {
				StartEnhance(firstFrame, render, completed);
			} else {
				StartReplace(render, completed);
			}
		}

		/// <summary>
		/// Tuät d Animazion looslah wo tuät vo zwe uif viär Bit erwiitärä
		/// </summary>
		/// 
		/// <remarks>
		/// S Timing wird wiä im Modus eis vo dr Animazion vorgäh, das heisst s 
		/// letschtä Biud vo VPM definiärt diä erschtä zwäi Bits unds jedes Biud
		/// vord Animazion tuät diä reschtlichä zwäi Bits ergänzä unds de uifd
		/// Viärbit-Queuä uisgäh.
		/// </remarks>
		/// 
		/// <param name="firstFrame">S Buid vo VPM wod Animazion losgla het</param>
		/// <param name="render">Ä Funktion wo tuät s Buid uisgäh</param>
		/// <param name="completed">Wird uisgfiärt wenn fertig</param>
		private void StartEnhance(byte[][] firstFrame, Action<byte[][]> render, Action completed = null)
		{
			_currentVpmFrame = firstFrame;
			if (Frames.Length == 1) {
				Logger.Info("[vni][{0}] Enhancing one frame ({1})", SwitchMode, Name);
				render(new []{ _currentVpmFrame[0], _currentVpmFrame[1], Frames[0].Planes[0].Plane, Frames[0].Planes[1].Plane });
				FinishIn(Frames[0].Delay, completed);
				return;
			}
			Logger.Info("[vni][{0}] Starting enhanced animation of {1} frames ({2})...", SwitchMode, Frames.Length, Name);
			_animation = _frames
				.Select(frame => new []{ _currentVpmFrame[0], _currentVpmFrame[1], frame.Planes[0].Plane, frame.Planes[1].Plane })
				.Do(_ => _frameIndex++)
				.Subscribe(render.Invoke, () => FinishIn(Frames[Frames.Count() - 1].Delay, completed));
		}

		/// <summary>
		/// Tuät d Animazion looslah und d Biudli uif diä entschprächendi Queuä
		/// uisgäh.
		/// </summary>
		/// 
		/// <remarks>
		/// Das hiä isch dr Fau wo diä gsamti Animazion uisgäh und VPM ignoriärt
		/// wird (dr Modus eis).
		/// </remarks>
		/// 
		/// <param name="render">Ä Funktion wo tuät s Buid uisgäh</param>
		/// <param name="completed">Wird uisgfiärt wenn fertig</param>
		private void StartReplace(Action<byte[][]> render, Action completed = null)
		{
			if (Frames.Length == 1) {
				Logger.Info("[vni][{0}] Replacing one frame ({1}).", SwitchMode, Name);
				render(Frames[0].PlaneData);
				FinishIn(Frames[0].Delay, completed);
				return;
			}
			Logger.Info("[vni][{0}] Starting colored gray4 animation of {1} frames ({2})...", SwitchMode, Frames.Length, Name);
			_animation = _frames
				.Do(_ => _frameIndex++)
				.Select(frame => frame.PlaneData)
				.Subscribe(render.Invoke, () => FinishIn(Frames[Frames.Length - 1].Delay, completed));
		}

		/// <summary>
		/// Tuät d Animazion nachärä gwissä Ziit aahautä
		/// </summary>
		/// <param name="milliseconds">Ziit i Millisekundä</param>
		/// <param name="completed">Dr Callback wo muäss uifgriäft wärdä</param>
		private void FinishIn(uint milliseconds, Action completed)
		{
			// nu uifs letschti biud wartä bis mer fertig sind
			Observable
				.Never<Unit>()
				.StartWith(Unit.Default)
				.Delay(TimeSpan.FromMilliseconds(milliseconds))
				.Subscribe(_ => {
					IsRunning = false;
					completed?.Invoke();
				});
		}

		/// <summary>
		/// Tuäts Frame vo VPM aktualisiärä, wo diä erschtä zwe Bits im 
		/// Modus <see cref="AddPlanes"/> definiärt.
		/// </summary>
		/// <param name="planes">S VPM Frame i Bitplanes uifgschplittet</param>
		public void NextFrame(byte[][] planes)
		{
			_currentVpmFrame = planes;
		}

		/// <summary>
		/// Tuät d Animazion aahautä.
		/// </summary>
		public void Stop()
		{
			_animation?.Dispose();
			IsRunning = false;
		}

		public bool Equals(Animation animation)
		{
			return Offset == animation.Offset;
		}

		public override string ToString()
		{
			return $"{Name}, {Frames.Length} frames";
		}
	}

	public enum AnimationEditMode
	{
		Replace, Mask, Fixed
	}
}
