using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using LibDmd.Common;
using LibDmd.Converter.Colorize;
using LibDmd.Input;
using LibDmd.Output;
using LibDmd.Output.FileOutput;
using LibDmd.Output.Network;
using LibDmd.Output.PinDmd1;
using LibDmd.Output.PinDmd2;
using LibDmd.Output.PinDmd3;
using Microsoft.Win32;
using NLog;

namespace LibDmd.DmdDevice
{
	public class Pin2Dmd : IDmdDevice
	{

		private VirtualDmd _dmd;
		
		private bool _colorize;
		private string _gameName;
		private Pin2DmdSource _source = new Pin2DmdSource();

		//endpoints for communication
		private const byte EP_IN = 0x81;
		private const byte EP_OUT = 0x01;

		private const byte SWITCH_MODE_PAL = 0;
		private const byte SWITCH_MODE_REPL = 1;
		private const byte SWITCH_MODE_COLMASK = 2;
		private const byte SWITCH_MODE_FOLLOW = 4;
		private const byte SWITCH_MODE_EVENT = 3;


		private byte[] ConsoleData = new byte[3];
		private byte ConsoleDataPtr = 0;
		private byte[] ConsoleInput = new byte[2];

		private Coloring coloring;
		private byte[][] outbuffer;
		private byte[][] animbuf;

		private byte[] oldbuffer;
		private ushort[] seg_data_old = new ushort[50];

		private uint Timer = 0;
		private uint lastTick = 0;
		private ushort nextPalette, defaultPalette, activePalette = 0;
		private bool vni_file = false;
		private byte fsq_version = 0;

		private BinaryReader FSQfile;
		private bool FSQisOpen = false;

		private static readonly string AssemblyPath = Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private List<PalMapping> listOfMappings;
		private FrameSeq actFrame;

		private Color[] CurrentPalette;

		public Pin2Dmd()
		{
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
		}

		private ushort readShort(BinaryReader reader)
		{
			var p = reader.ReadBytes(2);
			return (ushort) ((p[0] << 8) + p[1]);
		}

		private uint readInt(BinaryReader reader)
		{
			var p = reader.ReadBytes(4);
			return (uint) ((p[0] << 24) + (p[1] << 16) + (p[2] << 8) + p[3]);
		}

		private byte reverse_byte(byte a)
		{
			return (byte)(((a & 0x1) << 7) | ((a & 0x2) << 5) |
				((a & 0x4) << 3) | ((a & 0x8) << 1) |
				((a & 0x10) >> 1) | ((a & 0x20) >> 3) |
				((a & 0x40) >> 5) | ((a & 0x80) >> 7));
		}
		private FrameSeq read_replacement_frame_start(uint offset, byte switchmode)
		{
			var frameSeq = new FrameSeq {
				offset = offset,
				switchmode = switchmode
			};

			FSQfile.BaseStream.Seek(frameSeq.offset, SeekOrigin.Begin);
			if (!vni_file) {
				frameSeq.numberOfFrames = FSQfile.ReadInt16BE();
			} else {
				ushort tmp16 = FSQfile.ReadUInt16(); //size of scene name
				FSQfile.BaseStream.Seek(offset + tmp16 + 18, SeekOrigin.Begin); //skip scene name + unneeded data
				frameSeq.numberOfFrames = FSQfile.ReadInt16BE();
				//fread(buf, 1, 5, FSQfile);
				//fread(buf, 1, 2, FSQfile); //width
				//fread(buf, 1, 2, FSQfile); //height
				FSQfile.BaseStream.Seek(9, SeekOrigin.Current);
			}

			return frameSeq;
		}
							
		private byte[][] read_replacement_frame_next(FrameSeq frameSeq)
		{
			// read next frame
			uint read = 0;
			byte[][] planes;
			byte[] frame;
			if (!vni_file)
			{
				frameSeq.delay = FSQfile.ReadUInt32BE();
				frameSeq.numberOfPlanes = FSQfile.ReadUInt16BE();
				var planeSize = FSQfile.ReadUInt16BE();

				planes = new byte[frameSeq.numberOfPlanes][];

				if (fsq_version > 1)
				{
					frameSeq.crc32 = readInt(FSQfile);
				}
				for (int i = 0; i < frameSeq.numberOfPlanes; i++) {
					if (fsq_version > 1) {
						var marker = FSQfile.ReadByte();
						if (marker == 1) {
							frameSeq.mask = FSQfile.ReadBytes(planeSize);

						} else if (planeSize == 512) {
							planes[i] = FSQfile.ReadBytes(planeSize);
						}

					} else {
						if (planeSize == 512)
							planes[i] = FSQfile.ReadBytes(planeSize);
					}
				}

			} else {
				var planeSize = readShort(FSQfile);

				frameSeq.delay = readShort(FSQfile);
				frameSeq.crc32 = readInt(FSQfile);
				frameSeq.numberOfPlanes = FSQfile.ReadByte();

				planes = new byte[frameSeq.numberOfPlanes][];

				FSQfile.ReadByte(); // skip (compressed flag)

				for (int i = 0; i < frameSeq.numberOfPlanes; i++) {
					var marker = FSQfile.ReadByte(); // plane number
					if (marker == 0x6D) { // mask
						frameSeq.mask = FSQfile.ReadBytes(planeSize);

					} else {
						if (planeSize == 512)
							planes[i] = FSQfile.ReadBytes(planeSize);
					}
				}

				if ((frameSeq.actFrame < (frameSeq.numberOfFrames - 1)) && (frameSeq.switchmode == SWITCH_MODE_FOLLOW)) {
					var file_pos = FSQfile.BaseStream.Position; // store current position

					planeSize = readShort(FSQfile);

					FSQfile.ReadBytes(2); // skip delay
					frameSeq.crc32 = readInt(FSQfile);
					FSQfile.ReadBytes(1); // skip number of planes
					FSQfile.ReadBytes(1); // skip

					var marker = FSQfile.ReadByte(); // plane number
					if (marker == 0x6D) // mask
						frameSeq.mask = FSQfile.ReadBytes(planeSize);

					FSQfile.BaseStream.Seek(file_pos, SeekOrigin.Begin); //return to old offset
				}

				frame = planes.Where(p => p != null).SelectMany(p => p).ToArray();

				if (frameSeq.addPlanes)
					for (int i = 0; i < (2 * planeSize); i++)
						frame[i] = reverse_byte(frame[i + (2 * planeSize)]);
				else
					for (int i = 0; i < (4 * planeSize); i++)
						frame[i] = reverse_byte(frame[i]);

				planes = FrameUtil.SplitBitplanes(128, 32, frame);
			}

			frameSeq.actFrame++;
			return planes;
		}
	
		private FrameSeq read_replacement_frame_end()
		{
			return null;
		}
							
		private void checkTimer()
		{
			var tick = (uint)Environment.TickCount;
			var tickDiff = tick - lastTick;
			lastTick = tick;

			if (Timer > tickDiff)
				Timer -= tickDiff;
			else
				Timer = 0;
		}
		
		private Mapping lookupMapping(uint[] crc32)
		{
			return coloring.Mappings.FirstOrDefault(m => crc32.Contains(m.Checksum));
		}

		

		private uint calculate_crc32(byte[] input)
		{
			unchecked {
				var cs = (uint)(((uint)0) ^ (-1));
				var len = input.Length;
				for (var i = 0; i < len; i++) {
					cs = (cs >> 8) ^ table[(cs ^ input[i]) & 0xFF];
				}
				cs = (uint)(cs ^ (-1));

				if (cs < 0) {
					cs += (uint)4294967296;
				}
				return cs;
			}
		}

		private uint calculate_crc32_mask(byte[] input, byte[] mask)
		{
			if (mask == null) {
				return 0;
			}
			var maskedPlane = new byte[512];
			var plane = new BitArray(input);
			plane.And(new BitArray(mask)).CopyTo(maskedPlane, 0);
			return calculate_crc32(maskedPlane);
		}
		
		private void switchPalette(ushort palNumber)
		{
			if (palNumber == activePalette)
				return;
			Logger.Info("Switching to palette {0}", palNumber);
		}

		private void doReplay(FrameSeq actFrame)
		{
			if (actFrame != null && actFrame.newFrame) {
				actFrame.newFrame = false;
				RenderBuffer();
			}
		}
				
		private FrameSeq searchKeyFrame(FrameSeq af, byte[][] displaybuf, int numberOfPlanesToSearch)
		{
			uint[] crc32 = new uint[12]; // max 10 masks and one without
			uint follow_crc32;

			if (coloring == null) {
				return null;
			}

			for (int i = 0; i < numberOfPlanesToSearch; i++) {

				if (af != null && af.switchmode == SWITCH_MODE_FOLLOW) {
					follow_crc32 = calculate_crc32_mask(displaybuf[i], af.mask);
					if (follow_crc32 == af.crc32) {
						af.followMaskMatch = true;
						break;
					} else {
						if (i == numberOfPlanesToSearch - 1) {
							af = read_replacement_frame_end();
							Timer = 0;
						}
					}
				}

				crc32[0] = calculate_crc32(displaybuf[i]);
				for (int j = 0; j < coloring.Masks.Length; j++)
					crc32[j + 1] = calculate_crc32_mask(displaybuf[i], coloring.Masks[i]);

				var pPalMapping = lookupMapping(crc32);
				if (pPalMapping == null) {
					continue;
				}
				var switchMode = GetSwitchMode(pPalMapping.Mode);
				switch (switchMode) {
					case SWITCH_MODE_REPL:
					case SWITCH_MODE_COLMASK:
					case SWITCH_MODE_FOLLOW:
						if (af != null) {
							Timer = 0;
						}
						af = read_replacement_frame_start(pPalMapping.Offset, switchMode);
						af.addPlanes = (switchMode == SWITCH_MODE_FOLLOW || switchMode == SWITCH_MODE_COLMASK);
						nextPalette = pPalMapping.PaletteIndex;
						break;
					case SWITCH_MODE_PAL:
						if (af != null) {
							af = read_replacement_frame_end();
							Timer = 0;
						}
						if (pPalMapping.Duration != 0) {
							Timer = pPalMapping.Duration;
							nextPalette = defaultPalette;
							activePalette = pPalMapping.PaletteIndex;
						} else
							nextPalette = pPalMapping.PaletteIndex;
						break;
					case SWITCH_MODE_EVENT:
						break;
				}
				return af; // leave loop after first match
			}
			return af;
		}

		private bool moveToNextFrame(FrameSeq af)
		{
			if (af != null && af.switchmode == SWITCH_MODE_FOLLOW && af.followMaskMatch) {
				af.followMaskMatch = false;
				return true;
			}
			if (Timer == 0)
				return true;
			return false;
		}

		private FrameSeq checkEndOfPalSwitchOrReplay(FrameSeq af)
		{
			if (moveToNextFrame(af)) {  // in ms
				if (af != null) {
					if (af.actFrame < af.numberOfFrames) {

						animbuf = read_replacement_frame_next(af);
						Logger.Debug("Animation frame read ({0}).", af.addPlanes);

						af.newFrame = true;          // mark new frame to recreate rgb output buffer
						Timer = af.delay;
					} else {
						af = read_replacement_frame_end();
						nextPalette = defaultPalette;
					}
				}
				switchPalette(nextPalette);
			}

			return af;
		}

		private void Send_Clear_Settings()
		{
			Logger.Info("Clearing settings.");
		}

		private void Send_Clear_Screen()
		{
			Logger.Info("Clearing screen.");
		}

		private bool Check_Version()
		{
			Logger.Info("Checking for firmware, lol!");
			return true;
		}

		private void LoadPalette(string fileName)
		{
			coloring = new Coloring(fileName);
			vni_file = coloring.Version == 0x02;
		}


		public void Close()
		{
			Logger.Info("[vpm] Close()");
		}

		public void SetColorize(bool colorize)
		{
			_colorize = colorize;
		}

		public void SetGameName(string gameName)
		{
			_gameName = gameName;
		}

		public void SetColor(Color color)
		{
			if (!_colorize) {

				CurrentPalette = new Color[16];
				double R = 0, G = 0, B = 0;
				int i, r, g, b = 0;
				if (color.R > 0)
					R = (double)color.R / 255;
				if (color.G > 0)
					G = (double)color.G / 255;
				if (color.B > 0)
					B = (double)color.B / 255;
				for (i = 0; i < 16; i++) {
					r = (int)(R * (i * 17));
					g = (int)(G * (i * 17));
					b = (int)(B * (i * 17));
					if (r > 255)
						r = 255;
					if (g > 255)
						g = 255;
					if (b > 255)
						b = 255;
					CurrentPalette[i] = new Color { R = (byte)r, B = (byte)b, G = (byte)g };
				}

			} else {
				var palPath = Path.Combine(GetColorPath(), _gameName, "pin2dmd.pal");
				if (File.Exists(palPath)) {
					LoadPalette(palPath);

					fsq_version = 0;
					var aniPath = Path.Combine(GetColorPath(), _gameName, !vni_file ? "pin2dmd.fsq" : "pin2dmd.vni");
					if (File.Exists(aniPath)) {
						Logger.Info("Animation file found at {0}, loading.", aniPath);
						FSQfile = new BinaryReader(new FileStream(aniPath, FileMode.Open));
						FSQisOpen = true;
					} else {
						Logger.Warn("No animation file found at {0}, no animations.", aniPath);
					}

				} else {
					Logger.Warn("No palette file found at {0}, no coloring.", palPath);
				}

				if (FSQfile == null) {
					return;
				}

				if (!vni_file) {
					if (Encoding.Default.GetString(FSQfile.ReadBytes(4)) == "FSQ ") {
						fsq_version = FSQfile.ReadByte();
					}
				}

				FSQfile.BaseStream.Seek(0, SeekOrigin.Begin);
			}
		}

		public void LoadPalette(uint palIndex)
		{
			if (coloring.Palettes.Length < palIndex && coloring.Palettes[palIndex].Colors.Count() == 16) {
				CurrentPalette = coloring.Palettes[palIndex].Colors;
			} else {
				Logger.Warn("Cannot load palette {0} through console.", palIndex);
			}
		}

		public void RenderRgb24(int width, int height, byte[] frame)
		{
			Logger.Warn("Discarding RGB frame.");
		}

		public void RenderGray4(int width, int height, byte[] frame)
		{
			checkTimer();

			if (actFrame == null && FrameUtil.CompareBuffers(oldbuffer, 0, frame, 0, width * height)) //check if same frame again
				return;

			oldbuffer = frame;

			outbuffer = FrameUtil.Split(width, height, 4, frame);

			actFrame = searchKeyFrame(actFrame, outbuffer, 4);
			actFrame = checkEndOfPalSwitchOrReplay(actFrame);

			if (actFrame != null) {
				if (actFrame.addPlanes) {
					actFrame.newFrame = false;
				} else {
					outbuffer = animbuf;
					doReplay(actFrame);
				}
			} else
				RenderBuffer();
		}
		
		public void RenderGray2(int width, int height, byte[] frame)
		{
			checkTimer();

			if (FrameUtil.CompareBuffers(oldbuffer, 0, frame, 0, width * height)) { //check if same frame again

				// same frame
				if (actFrame != null && !actFrame.addPlanes && actFrame.numberOfFrames != 1) {
					actFrame = checkEndOfPalSwitchOrReplay(actFrame);
					outbuffer = animbuf;
					doReplay(actFrame);
				} else
					return;
			} else {

				// different frame
				oldbuffer = frame;

				outbuffer = FrameUtil.Split(width, height, 4, frame.Select(pixel => {
					if (pixel == 3) {
						return (byte)15;
					}
					if (pixel == 2) {
						return (byte)4;
					}
					return pixel;
				}).ToArray());

				if (actFrame != null && (actFrame.addPlanes || actFrame.numberOfFrames == 1))
					Timer = 0;

				actFrame = searchKeyFrame(actFrame, outbuffer, 4);
				actFrame = checkEndOfPalSwitchOrReplay(actFrame);

				if (actFrame != null) {
					if (actFrame.addPlanes) {

						outbuffer[1] = outbuffer[2];
						outbuffer[2] = animbuf[0];
						outbuffer[3] = animbuf[1];

						// recreate rgb buffer, if either pin input changed or color mask
						RenderBuffer();
						actFrame.newFrame = false;

					} else {
						outbuffer = animbuf;
						doReplay(actFrame);
					}

				} else
					RenderBuffer();

			}
		}

		public void RenderAlphaNumeric(NumericalLayout numericalLayout, ushort[] readUInt16Array, ushort[] ushorts)
		{
		}
		

		public void SetPalette(Color[] colors)
		{
			if (colors.Length == 4) {
				CurrentPalette = new[] {
				colors[0],
				colors[1],
				new Color { R = 0x22, G = 0x0, B = 0x0 },
				new Color { R = 0x33, G = 0x0, B = 0x0 },
				colors[2],
				new Color { R = 0x55, G = 0x0, B = 0x0 },
				new Color { R = 0x66, G = 0x0, B = 0x0 },
				new Color { R = 0x77, G = 0x0, B = 0x0 },
				new Color { R = 0x88, G = 0x0, B = 0x0 },
				new Color { R = 0x99, G = 0x0, B = 0x0 },
				new Color { R = 0xaa, G = 0x0, B = 0x0 },
				new Color { R = 0xbb, G = 0x0, B = 0x0 },
				new Color { R = 0xcc, G = 0x0, B = 0x0 },
				new Color { R = 0xdd, G = 0x0, B = 0x0 },
				new Color { R = 0xee, G = 0x0, B = 0x0 },
				colors[3]
			};

			} else if (colors.Length == 16) {
				CurrentPalette = colors;

			} else {
				Logger.Warn("Not setting {0}-color palette.");
			}
		}

		private void RenderBuffer()
		{
			_source.RenderFrame(new Tuple<byte[][], Color[]>(outbuffer, CurrentPalette));
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct PMoptions
		{
			public int Red, Green, Blue;
			public int Perc66, Perc33, Perc0;
			public int DmdOnly, Compact, Antialias;
			public int Colorize;
			public int Red66, Green66, Blue66;
			public int Red33, Green33, Blue33;
			public int Red0, Green0, Blue0;
		}


		public void Init()
		{
			if (_dmd == null) {
					Logger.Info("Opening virtual DMD...");
					CreateVirtualDmd();

				} else {
					_dmd.Dispatcher.Invoke(() => {
						SetupGraphs();
						SetupVirtualDmd();
					});
				}
		}


		private void CreateVirtualDmd()
		{
			var thread = new Thread(() => {

				_dmd = new VirtualDmd();
				SetupGraphs();

				// Create our context, and install it:
				SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

				// When the window closes, shut down the dispatcher
				_dmd.Closed += (s, e) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
				_dmd.Dispatcher.Invoke(SetupVirtualDmd);

				// Start the Dispatcher Processing
				Dispatcher.Run();
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
		}

		private void SetupGraphs()
		{
			var graph = new RenderGraph {
					Name = "2-bit Colored VPM Graph",
					Source = _source,
					Destinations = new List<IDestination> {_dmd.Dmd},
				};
			graph.Init();
			graph.StartRendering();
		}

		/// <summary>
		/// Sets the virtual DMD's parameters, initializes it and shows it.
		/// </summary>
		private void SetupVirtualDmd()
		{
			_dmd.Dmd.Init();
			_dmd.Show();
		}
		
		private static string GetColorPath()
		{
			// first, try executing assembly.
			var altcolor = Path.Combine(AssemblyPath, "altcolor");
			if (Directory.Exists(altcolor)) {
				Logger.Info("Determined color path from assembly path: {0}", altcolor);
				return altcolor;
			}

			// then, try vpinmame location
			var vpmPath = GetDllPath("VPinMAME.dll");
			if (vpmPath == null) {
				return null;
			}
			altcolor = Path.Combine(Path.GetDirectoryName(vpmPath), "altcolor");
			if (Directory.Exists(altcolor)) {
				Logger.Info("Determined color path from VPinMAME.dll location: {0}", altcolor);
				return altcolor;
			}
			Logger.Info("No altcolor folder found, ignoring palettes.");
			return null;
		}

		private static string GetDllPath(string name)
		{
			const int maxPath = 260;
			var builder = new StringBuilder(maxPath);
			var hModule = GetModuleHandle(name);
			if (hModule == IntPtr.Zero) {
				return null;
			}
			var size = GetModuleFileName(hModule, builder, builder.Capacity);
			return size <= 0 ? null : builder.ToString();
		}

		private static readonly uint[] table = {
				0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA, 0x076DC419, 0x706AF48F,
				0xE963A535, 0x9E6495A3, 0x0EDB8832, 0x79DCB8A4, 0xE0D5E91E, 0x97D2D988,
				0x09B64C2B, 0x7EB17CBD, 0xE7B82D07, 0x90BF1D91, 0x1DB71064, 0x6AB020F2,
				0xF3B97148, 0x84BE41DE, 0x1ADAD47D, 0x6DDDE4EB, 0xF4D4B551, 0x83D385C7,
				0x136C9856, 0x646BA8C0, 0xFD62F97A, 0x8A65C9EC, 0x14015C4F, 0x63066CD9,
				0xFA0F3D63, 0x8D080DF5, 0x3B6E20C8, 0x4C69105E, 0xD56041E4, 0xA2677172,
				0x3C03E4D1, 0x4B04D447, 0xD20D85FD, 0xA50AB56B, 0x35B5A8FA, 0x42B2986C,
				0xDBBBC9D6, 0xACBCF940, 0x32D86CE3, 0x45DF5C75, 0xDCD60DCF, 0xABD13D59,
				0x26D930AC, 0x51DE003A, 0xC8D75180, 0xBFD06116, 0x21B4F4B5, 0x56B3C423,
				0xCFBA9599, 0xB8BDA50F, 0x2802B89E, 0x5F058808, 0xC60CD9B2, 0xB10BE924,
				0x2F6F7C87, 0x58684C11, 0xC1611DAB, 0xB6662D3D, 0x76DC4190, 0x01DB7106,
				0x98D220BC, 0xEFD5102A, 0x71B18589, 0x06B6B51F, 0x9FBFE4A5, 0xE8B8D433,
				0x7807C9A2, 0x0F00F934, 0x9609A88E, 0xE10E9818, 0x7F6A0DBB, 0x086D3D2D,
				0x91646C97, 0xE6635C01, 0x6B6B51F4, 0x1C6C6162, 0x856530D8, 0xF262004E,
				0x6C0695ED, 0x1B01A57B, 0x8208F4C1, 0xF50FC457, 0x65B0D9C6, 0x12B7E950,
				0x8BBEB8EA, 0xFCB9887C, 0x62DD1DDF, 0x15DA2D49, 0x8CD37CF3, 0xFBD44C65,
				0x4DB26158, 0x3AB551CE, 0xA3BC0074, 0xD4BB30E2, 0x4ADFA541, 0x3DD895D7,
				0xA4D1C46D, 0xD3D6F4FB, 0x4369E96A, 0x346ED9FC, 0xAD678846, 0xDA60B8D0,
				0x44042D73, 0x33031DE5, 0xAA0A4C5F, 0xDD0D7CC9, 0x5005713C, 0x270241AA,
				0xBE0B1010, 0xC90C2086, 0x5768B525, 0x206F85B3, 0xB966D409, 0xCE61E49F,
				0x5EDEF90E, 0x29D9C998, 0xB0D09822, 0xC7D7A8B4, 0x59B33D17, 0x2EB40D81,
				0xB7BD5C3B, 0xC0BA6CAD, 0xEDB88320, 0x9ABFB3B6, 0x03B6E20C, 0x74B1D29A,
				0xEAD54739, 0x9DD277AF, 0x04DB2615, 0x73DC1683, 0xE3630B12, 0x94643B84,
				0x0D6D6A3E, 0x7A6A5AA8, 0xE40ECF0B, 0x9309FF9D, 0x0A00AE27, 0x7D079EB1,
				0xF00F9344, 0x8708A3D2, 0x1E01F268, 0x6906C2FE, 0xF762575D, 0x806567CB,
				0x196C3671, 0x6E6B06E7, 0xFED41B76, 0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC,
				0xF9B9DF6F, 0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5, 0xD6D6A3E8, 0xA1D1937E,
				0x38D8C2C4, 0x4FDFF252, 0xD1BB67F1, 0xA6BC5767, 0x3FB506DD, 0x48B2364B,
				0xD80D2BDA, 0xAF0A1B4C, 0x36034AF6, 0x41047A60, 0xDF60EFC3, 0xA867DF55,
				0x316E8EEF, 0x4669BE79, 0xCB61B38C, 0xBC66831A, 0x256FD2A0, 0x5268E236,
				0xCC0C7795, 0xBB0B4703, 0x220216B9, 0x5505262F, 0xC5BA3BBE, 0xB2BD0B28,
				0x2BB45A92, 0x5CB36A04, 0xC2D7FFA7, 0xB5D0CF31, 0x2CD99E8B, 0x5BDEAE1D,
				0x9B64C2B0, 0xEC63F226, 0x756AA39C, 0x026D930A, 0x9C0906A9, 0xEB0E363F,
				0x72076785, 0x05005713, 0x95BF4A82, 0xE2B87A14, 0x7BB12BAE, 0x0CB61B38,
				0x92D28E9B, 0xE5D5BE0D, 0x7CDCEFB7, 0x0BDBDF21, 0x86D3D2D4, 0xF1D4E242,
				0x68DDB3F8, 0x1FDA836E, 0x81BE16CD, 0xF6B9265B, 0x6FB077E1, 0x18B74777,
				0x88085AE6, 0xFF0F6A70, 0x66063BCA, 0x11010B5C, 0x8F659EFF, 0xF862AE69,
				0x616BFFD3, 0x166CCF45, 0xA00AE278, 0xD70DD2EE, 0x4E048354, 0x3903B3C2,
				0xA7672661, 0xD06016F7, 0x4969474D, 0x3E6E77DB, 0xAED16A4A, 0xD9D65ADC,
				0x40DF0B66, 0x37D83BF0, 0xA9BCAE53, 0xDEBB9EC5, 0x47B2CF7F, 0x30B5FFE9,
				0xBDBDF21C, 0xCABAC28A, 0x53B39330, 0x24B4A3A6, 0xBAD03605, 0xCDD70693,
				0x54DE5729, 0x23D967BF, 0xB3667A2E, 0xC4614AB8, 0x5D681B02, 0x2A6F2B94,
				0xB40BBE37, 0xC30C8EA1, 0x5A05DF1B, 0x2D02EF8D
			};

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		public static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("kernel32.dll", SetLastError = true)]
		[PreserveSig]
		public static extern uint GetModuleFileName
		(
			[In] IntPtr hModule,
			[Out] StringBuilder lpFilename,
			[In][MarshalAs(UnmanagedType.U4)] int nSize
		);

		private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			var ex = e.ExceptionObject as Exception;
			if (ex != null) {
				Logger.Error(ex.ToString());
			}
		}

		private byte GetSwitchMode(SwitchMode switchMode)
		{
			switch (switchMode)
			{
				case SwitchMode.Palette: return SWITCH_MODE_PAL;
				case SwitchMode.Replace: return SWITCH_MODE_REPL;
				case SwitchMode.ColorMask: return SWITCH_MODE_COLMASK;
				case SwitchMode.Event: return SWITCH_MODE_EVENT;
				case SwitchMode.Follow: return SWITCH_MODE_FOLLOW;
				default:
					throw new ArgumentOutOfRangeException(nameof(switchMode), switchMode, null);
			}
		}
	}

	internal class Pin2DmdSource : AbstractSource, IColoredGray4Source
	{
		public override string Name { get; } = "Pin2Dmd";
		public IObservable<Unit> OnResume { get; }
		public IObservable<Unit> OnPause { get; }
		private readonly Subject<Tuple<byte[][], Color[]>> _frames = new Subject<Tuple<byte[][], Color[]>>();
		public IObservable<Tuple<byte[][], Color[]>> GetColoredGray4Frames()
		{
			return _frames;
		}
		public void RenderFrame(Tuple<byte[][], Color[]> frame)
		{
			_frames.OnNext(frame);
		}
	}

	internal class FrameSeq
	{
		internal int numberOfFrames; // number of frames contained in this sequence
		internal int actFrame; // act number of frame that is displayed
		internal uint offset; // offset of sequence in file
		internal uint delay; // delay in ms for act frame
		internal int numberOfPlanes; // number of planes used when replaying replacement frames
		internal bool newFrame; // set when new frame to display was loaded
		internal bool addPlanes; // set to true, if planes should be added instead of being replaced
		internal uint crc32; // next hash to look for (in col seq mode)
		internal byte[] mask; // mask
		internal byte switchmode;
		internal bool followMaskMatch;
	}

	internal class PalMapping
	{
		internal uint crc32;
		internal byte switchmode;
		internal ushort palIndex;
		internal uint durationInMillis;
	}
}
