using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace PictureShrinkerGrad
{
	class Program
	{
		private class Step
		{
			public double Cost;
			public int PrevOffset;

			public Step ( double cost, int prev )
			{
				Cost = cost;
				PrevOffset = prev;
			}
		}

		private static Color getPic ( Color[ , ] pic, int x, int y, bool flip )
		{
			return flip ? pic[ y, x ] : pic[ x, y ];
		}

		private static void setPic ( Color[ , ] pic, int x, int y, Color c, bool flip )
		{
			if ( flip )
				pic[ y, x ] = c;
			else
				pic[ x, y ] = c;
		}

		internal static void Main ()
		{
			Console.WriteLine ( "Welcome!" );

			const string path = @".\PicShrinks\";
			const string filename = @"picture.png";
			var rnd = new Random ();
			var timer = new Stopwatch ();

			const int shrink_amt_px_w = 500;
			const int shrink_amt_px_h = 500;
			const bool maintain_aspect_ratio = true;
			const int max_shift = 1;
			const double grad_noise_amp = 0.05;

			Color[ , ] pic;
			int picw, pich;
			using ( var bmp = new Bitmap ( Path.Combine ( path, filename ) ) )
			{
				picw = bmp.Width;
				pich = bmp.Height;

				pic = new Color[ picw, pich ];
				for ( int x = 0; x < picw; x++ )
				{
					for ( int y = 0; y < pich; y++ )
						pic[ x, y ] = bmp.GetPixel ( x, y );
				}
			}

			int deltaW = shrink_amt_px_w;
			int deltaH = maintain_aspect_ratio ? shrink_amt_px_w * pich / picw : shrink_amt_px_h;

			int targetW = picw - deltaW;
			int targetH = pich - deltaH;
			if ( targetW <= 5 || targetH <= 5 )
			{
				Console.WriteLine ( "Too much shrinking! Target W: " + targetW + ", target H: " + targetH );
				int offset = Math.Min ( targetW, targetH );
				targetW += 5 - offset;
				targetH += 5 - offset;

				Console.WriteLine ( "New Target W: " + targetW + ", target H: " + targetH );
			}

			string dir = Path.Combine ( path, Path.Combine ( Path.GetFileNameWithoutExtension ( path ), DateTime.Now.ToString ( "yyyy-MM-dd HH-mm " ) + Path.GetFileNameWithoutExtension ( filename ) ) );
			Directory.CreateDirectory ( dir );

			dumpPicToBmp ( pic, picw, pich, Path.Combine ( dir, "Original.png" ) );

			double w2h = targetW * 1.0 / targetH;
			bool vert = false;
			timer.Start ();

			int shrinkNo = 1;
			//for ( shrinkNo = 1; shrinkNo <= shrinkAmtPx * 2; shrinkNo++ )
			while ( picw > targetW || pich > targetH )
			{
				int w = vert ? pich : picw;
				int h = vert ? picw : pich;

				// Build horizontal gradient map
				var grads = new double[ w - 1, h ];
				double maxg = double.MinValue, ming = double.MaxValue;
				ParallelRunnerNs.ParallelRunner.RunInParallel ( y =>
				//for(int y=0;y<h;y++)
				{
					for ( int x = 0; x < w - 1; x++ )
					{
						grads[ x, y ] = get4pxContrastGrad ( pic, w, h, x, y, vert ) + rnd.NextDouble () * grad_noise_amp;
						//grads[ x , y ] = get4pxMeanGrad ( pic, w, h, x, y, vert ) + rnd.NextDouble () * grad_noise_amp; // best so far

						maxg = Math.Max ( maxg, grads[ x, y ] );
						ming = Math.Min ( ming, grads[ x, y ] );
					}
				}, h );
				//dumpPicToBmp ( grads, w, h, maxg, ming, Path.Combine ( dir, "Original grads.png" ) );

				// Create gradient cost mapping
				var pathMap = new Step[ h, w - 1 ]; // [line][row]
				for ( int line = 0; line < h; line++ )
				{
					for ( int row = 0; row < w - 1; row++ )
					{
						if ( line == 0 )
							pathMap[ line, row ] = new Step ( grads[ row, line ], 0 );
						else
						{
							int prev = 0;
							double minGrad = 9e99;
							for ( int i = -max_shift; i <= max_shift; i++ )
							{
								if ( row + i < 0 || row + i > w - 2 )
									continue;

								double curGrad = grads[ row, line ] + pathMap[ line - 1, row + i ].Cost;
								if ( curGrad < minGrad )
								{
									minGrad = curGrad;
									prev = i;
								}
							}

							pathMap[ line, row ] = new Step ( minGrad, prev );
						}
					}
				}

				/*using ( var sw = new StreamWriter ( Path.Combine ( dir, "Path Map "+shrinkNo.ToString("0000")+".log"), false ))
				{
					for ( int line = 0; line < h; line++ )
					{
						for ( int row = 0; row < w - 1; row++ )
							sw.Write ( pathMap[ line, row ].Cost.ToString ( "0.000" ) + "\t" );

						sw.WriteLine ();
					}
				}//*/

				// Find cheapest verical path
				var cheapestRows = new int[ h ];

				double minCost = 9e99;
				for ( int row = 0; row < w - 1; row++ )
				{
					if ( pathMap[ h - 1, row ].Cost >= minCost )
						continue;

					minCost = pathMap[ h - 1, row ].Cost;
					cheapestRows[ h - 1 ] = row;
				}

				for ( int line = h - 2; line >= 0; line-- )
					cheapestRows[ line ] = cheapestRows[ line + 1 ] + pathMap[ line + 1, cheapestRows[ line + 1 ] ].PrevOffset;

				/***
				for ( int line = 0; line < h; line++ )
					grads[ cheapestRows[ line ], line ] = maxg;
				new Action<double[ , ], int, int, double, double, string> ( dumpPicToBmp2 ).BeginInvoke ( grads, w - 1, h, maxg, ming, Path.Combine ( dir, "Shrink " + shrinkNo.ToString ( "0000" ) + " path.png" ), null, null );//*/

				// Perform shrinking
				var newPic = new Color[ vert ? picw : picw - 1, vert ? pich - 1 : pich ];

				ParallelRunnerNs.ParallelRunner.RunInParallel ( y =>
				//for(int y=0;y<h;y++)
				{
					int theRow = cheapestRows[ y ];

					for ( int x = 0; x < theRow; x++ )
						setPic ( newPic, x, y, getPic ( pic, x, y, vert ), vert );

					int r = getPic ( pic, theRow, y, vert ).R + getPic ( pic, theRow + 1, y, vert ).R;
					int g = getPic ( pic, theRow, y, vert ).G + getPic ( pic, theRow + 1, y, vert ).G;
					int b = getPic ( pic, theRow, y, vert ).B + getPic ( pic, theRow + 1, y, vert ).B;

					setPic ( newPic, theRow, y, Color.FromArgb ( r / 2, g / 2, b / 2 ), vert );

					for ( int x = theRow + 2; x < w; x++ )
						setPic ( newPic, x - 1, y, getPic ( pic, x, y, vert ), vert );
				}, h );

				pic = newPic;
				if ( vert )
					pich--;
				else
					picw--;

				if ( shrinkNo % 20 == 0 )
					new Action<Color[ , ], int, int, string> ( dumpPicToBmp ).BeginInvoke ( pic, picw, pich, Path.Combine ( dir, "Shrink " + shrinkNo.ToString ( "0000" ) + ".png" ), null, null );
				shrinkNo++;

				if ( picw == targetW )
					vert = true;
				else if ( pich == targetH )
					vert = false;
				else
				{
					double ratio = picw * 1.0 / pich;

					vert = ratio < w2h;
				}
			}

			timer.Stop ();
			Console.WriteLine ( "Total time spent: " + timer.Elapsed.ToString ( "g" ) );

			Console.WriteLine ( "Done!" );
			Console.ReadKey ( true );
		}

		private static double get4pxHorizGrad ( Color[ , ] pic, int w, int h, int x, int y, bool flip )
		{
			double grad = getColorRgbDistSq (
								 getPic ( pic, Math.Max ( 0, x - 1 ), y, flip ),
								 getPic ( pic, x + 1, y, flip ) ) +
					getColorRgbDistSq (
								 getPic ( pic, x, y, flip ),
								 getPic ( pic, Math.Min ( w - 1, x + 2 ), y, flip ) );

			return grad / 2;
		}

		private static double get4pxMeanGrad ( Color[ , ] pic, int w, int h, int x, int y, bool flip )
		{
			double meanr = 0, meang = 0, meanb = 0;

			for ( int i = x - 1; i <= x + 2; i++ )
			{
				Color c = getPic ( pic, Math.Max ( 0, Math.Min ( w - 1, i ) ), y, flip );
				meanr += c.R;
				meang += c.G;
				meanb += c.B;
			}

			meanr /= 4;
			meang /= 4;
			meanb /= 4;

			double sdr = 0, sdg = 0, sdb = 0;

			for ( int i = x - 1; i <= x + 2; i++ )
			{
				Color c = getPic ( pic, Math.Max ( 0, Math.Min ( w - 1, i ) ), y, flip );
				sdr += ( c.R - meanr ) * ( c.R - meanr );
				sdg += ( c.G - meang ) * ( c.G - meang );
				sdb += ( c.B - meanb ) * ( c.B - meanb );
			}

			sdr /= 4;
			sdg /= 4;
			sdb /= 4;

			return ( sdr + sdg + sdb ) / 3;
		}

		private static double get4pxContrastGrad ( Color[ , ] pic, int w, int h, int x, int y, bool flip )
		{
			double grad = getColorContractRatio (
									getPic ( pic, Math.Max ( 0, x - 1 ), y, flip ),
									getPic ( pic, x + 1, y, flip ) ) +
					getColorContractRatio (
									getPic ( pic, x, y, flip ),
									getPic ( pic, Math.Min ( w - 1, x + 2 ), y, flip ) );

			return grad / 2;
		}

		private static double getColorRgbDistSq ( Color c1, Color c2 )
		{
			double dr = ( c1.R - c2.R ) / 255.0;
			double dg = ( c1.G - c2.G ) / 255.0;
			double db = ( c1.B - c2.B ) / 255.0;

			return Math.Sqrt ( dr * dr + dg * dg + db * db );
		}

		private static double getColorHsbDistSq ( Color c1, Color c2 )
		{
			float dh = ( c1.GetHue () - c2.GetHue () ) / 90;
			float ds = c1.GetSaturation () - c2.GetSaturation ();
			float db = c1.GetBrightness () - c2.GetBrightness ();

			return dh * dh + ds * ds + db * db;
		}

		private static double getColorContractRatio ( Color c1, Color c2 )
		{
			double lum1 = getRelLum ( c1 ) + 0.05;
			double lum2 = getRelLum ( c2 ) + 0.05;

			if ( lum1 > lum2 )
				return lum1 / lum2;

			return lum2 / lum1;
		}

		private static double getRelLum ( Color c )
		{
			double r = c.R / 255.0;
			double g = c.G / 255.0;
			double b = c.B / 255.0;

			if ( r <= 0.03928 )
				r = r / 12.92;
			else
				r = Math.Pow ( ( r + 0.055 ) / 1.055, 2.4 );

			if ( g <= 0.03928 )
				g = g / 12.92;
			else
				g = Math.Pow ( ( g + 0.055 ) / 1.055, 2.4 );

			if ( b <= 0.03928 )
				b = b / 12.92;
			else
				b = Math.Pow ( ( b + 0.055 ) / 1.055, 2.4 );

			return 0.2126 * r + 0.7152 * g + 0.0722 * b;
		}

		private static void dumpPicToBmp ( Color[ , ] pic, int w, int h, string filename )
		{
			using ( var b = new Bitmap ( w, h ) )
			{
				for ( int y = 0; y < h; y++ )
					for ( int x = 0; x < w; x++ )
						b.SetPixel ( x, y, pic[ x, y ] );

				b.Save ( filename );
			}

			Console.WriteLine ( "Pic " + Path.GetFileName ( filename ) + " stored!" );
		}

		private static void dumpPicToBmp2 ( double[ , ] pic, int w, int h, double max, double min, string filename )
		{
			double div = max - min;

			using ( var b = new Bitmap ( w, h ) )
			{
				for ( int y = 0; y < h; y++ )
					for ( int x = 0; x < w; x++ )
					{
						int c = ( int ) ( ( pic[ x, y ] - min ) / div * 255.5 );
						b.SetPixel ( x, y, Color.FromArgb ( c, c, c ) );
					}

				b.Save ( filename );
			}

			Console.WriteLine ( "Pic " + Path.GetFileName ( filename ) + " stored!" );
		}

		private static double getMax ( double[ , ] arr )
		{
			double max = double.MinValue;

			foreach ( double d in arr )
				if ( max < d )
					max = d;

			return max;
		}

		private static double getMin ( double[ , ] arr )
		{
			double min = double.MinValue;

			foreach ( double d in arr )
				if ( min > d )
					min = d;

			return min;
		}
	}
}
