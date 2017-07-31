using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibDmd.Common;
using LibDmd.Input.FileSystem;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public class AnimationSet
	{
		public readonly int Version;
		public readonly List<Animation> Animations;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public AnimationSet(string filename)
		{
			var fs = new FileStream(filename, FileMode.Open);
			var reader = new BinaryReader(fs);

			// name
			var header = Encoding.UTF8.GetString(reader.ReadBytes(4));
			if (header != "VPIN") {
				throw new WrongFormatException("Not a VPIN file: " + filename);
			}

			// version
			Version = reader.ReadInt16BE();

			// number of animations
			var numAnimations = reader.ReadInt16BE();

			Animations = new List<Animation>(numAnimations);
			Logger.Trace("Reading {0} animations...", numAnimations);
			for (var i = 0; i < numAnimations; i++) {
				Animations.Add(new Animation(reader));
			}
		}

		public override string ToString()
		{
			return $"VPIN v{Version}, {Animations.Count} animation(s)";
		}
	}
}
