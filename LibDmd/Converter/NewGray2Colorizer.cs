using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using LibDmd.Converter.Colorize;
using LibDmd.Input;

namespace LibDmd.Converter
{
	public class NewGray2Colorizer : AbstractSource, IConverter, IColoredGray2Source, IColoredGray4Source
	{
		public override string Name { get; } = "NEW 2-Bit Colorizer";
		public FrameFormat From { get; } = FrameFormat.Gray2;
		public IObservable<Unit> OnResume { get; }
		public IObservable<Unit> OnPause { get; }

		protected readonly Subject<Tuple<byte[][], Color[]>> ColoredGray2AnimationFrames = new Subject<Tuple<byte[][], Color[]>>();
		protected readonly Subject<Tuple<byte[][], Color[]>> ColoredGray4AnimationFrames = new Subject<Tuple<byte[][], Color[]>>();

		protected byte[] ColoredFrame;

		private readonly Coloring _coloring;
		private readonly AnimationSet _animations;

		public NewGray2Colorizer(Coloring coloring, AnimationSet animations)
		{
			_coloring = coloring;
			_animations = animations;
		}

		public void Init()
		{
			Dimensions.Subscribe(dim => ColoredFrame = new byte[dim.Width * dim.Height * 3]);
		}

		public void Convert(byte[] @from)
		{
			throw new NotImplementedException();
		}

		public IObservable<Tuple<byte[][], Color[]>> GetColoredGray2Frames()
		{
			return ColoredGray2AnimationFrames;
		}

		public IObservable<Tuple<byte[][], Color[]>> GetColoredGray4Frames()
		{
			return ColoredGray4AnimationFrames;
		}
	}
}
