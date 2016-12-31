﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using SharpMath;
using SharpMath.FFT;
using ImageUtility;
using Renderer;

namespace TestFourier
{
	public partial class FourierTestForm : Form {
		const int		SIGNAL_SIZE = 1024;

		#region NESTED TYPES

		[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
		struct CB_Main {
			public uint		_resolutionX;
			public uint		_resolutionY;
			public uint		_signalSize;
			public uint		_signalFlags;
			public float	_time;
			public float3	__pad;
		}

		#endregion

		#region FIELDS

		float4			m_black = float4.UnitW;
		float4			m_red = new float4( 1, 0, 0, 1 );
		float4			m_blue = new float4( 0, 0, 1, 1 );

		ImageFile		m_image = null;

		Device			m_device = new Device();

		FFT1D_GPU		m_FFT1D_GPU = null;

		// Direct GPU feed
		ConstantBuffer<CB_Main>		m_CB_Main;
		Shader						m_Shader_GenerateSignal;
		Shader						m_Shader_Display;
		Texture2D					m_texSpectrumCopy;

		// FFTW test
		fftwlib.FFT2D	m_FFTW_1D;

		#endregion

		public FourierTestForm() {
			InitializeComponent();
		}

		protected override void OnLoad( EventArgs e ) {
			base.OnLoad( e );

			try {
//				m_device.Init( viewportPanel.Handle, false, true );
				m_device.Init( imagePanel.Handle, false, true );
				m_FFT1D_GPU = new FFT1D_GPU( m_device, SIGNAL_SIZE );

				m_CB_Main = new ConstantBuffer<CB_Main>( m_device, 0 );
				m_Shader_GenerateSignal = new Shader( m_device, new System.IO.FileInfo( "./Shaders/GenerateSignal.hlsl" ), VERTEX_FORMAT.Pt4, "VS", null, "PS", null );
				m_Shader_Display = new Shader( m_device, new System.IO.FileInfo( "./Shaders/Display.hlsl" ), VERTEX_FORMAT.Pt4, "VS", null, "PS", null );
				m_texSpectrumCopy = new Texture2D( m_device, SIGNAL_SIZE, 1, 1, 1, PIXEL_FORMAT.RG32_FLOAT, false, true, null );

			} catch ( Exception ) {
				MessageBox.Show( "Failed to initialize DirectX device! Can't execute GPU FFT!", "DirectX Error", MessageBoxButtons.OK, MessageBoxIcon.Error );
				m_device = null;
			}

			m_image = new ImageFile( (uint) imagePanel.Width, (uint) imagePanel.Height, ImageFile.PIXEL_FORMAT.RGBA8, new ColorProfile( ColorProfile.STANDARD_PROFILE.sRGB ) );

			try {
				m_FFTW_1D = new fftwlib.FFT2D( SIGNAL_SIZE, 1 );	// Allocate on the fly, if the interop fails then it will crash immediately but at least the 
			} catch ( Exception _e ) {
				MessageBox.Show( "Failed to initialize reference FFTW library! It's not necessary for the unit test unless you need to compare against \"ground truth\".\r\nReason:\r\n" + _e.Message, "FFTW Library Error", MessageBoxButtons.OK, MessageBoxIcon.Error );
				m_FFTW_1D = null;
			}

			imagePanel.SkipPaint = checkBoxGPU.Checked;

			UpdateGraph1D();

			Application.Idle += Application_Idle;
		}

		protected override void OnClosing( CancelEventArgs e ) {
			if ( m_device != null ) {
				m_texSpectrumCopy.Dispose();
				m_Shader_Display.Dispose();
				m_Shader_GenerateSignal.Dispose();
				m_CB_Main.Dispose();

				m_FFT1D_GPU.Dispose();
				m_device.Dispose();
			}

			if ( m_FFTW_1D != null )
				m_FFTW_1D.Dispose();

			base.OnClosing( e );
		}

		void Application_Idle( object sender, EventArgs e ) {
			if ( tabControl1.SelectedTab == tabPage1D )
				UpdateGraph1D();
			else
				UpdateGraph2D();
		}

		DateTime	m_startTime = DateTime.Now;

		#region FFT 1D Test

		Complex[]		m_signalSource = new Complex[SIGNAL_SIZE];
		Complex[]		m_spectrum = new Complex[SIGNAL_SIZE];
		Complex[]		m_signalReconstructed = new Complex[SIGNAL_SIZE];

		void	UpdateGraph1D() {

			double	time = (DateTime.Now - m_startTime).TotalSeconds;

			if ( checkBoxGPU.Checked ) {
				UpdateGraph1D_GPU( time );
				return;
			}

			TestTransform1D( time );

			m_image.Clear( float4.One );

			float2	rangeX = new float2( 0.0f, SIGNAL_SIZE );
			float2	rangeY = new float2( -1, 1 );

			// Plot input signal
			if ( checkBoxShowInput.Checked ) {
//				m_image.PlotGraphAutoRangeY( m_black, rangeX, ref rangeY, ( float x ) => {
				m_image.PlotGraph( m_black, rangeX, rangeY, ( float x ) => {
					int		X = Math.Max( 0, Math.Min( SIGNAL_SIZE-1, (int) x ) );
					return (float) m_signalSource[X].r;
				} );
			}

			// Plot reconstructed signals (Real and Imaginary parts)
			if ( checkBoxShowReconstructedSignal.Checked ) {
				m_image.PlotGraph( m_red, rangeX, rangeY, ( float x ) => {
					int		X = Math.Max( 0, Math.Min( SIGNAL_SIZE-1, (int) x ) );
					return (float) m_signalReconstructed[X].r;
				} );
				m_image.PlotGraph( m_blue, rangeX, rangeY, ( float x ) => {
					int		X = Math.Max( 0, Math.Min( SIGNAL_SIZE-1, (int) x ) );
					return (float) m_signalReconstructed[X].i;
				} );
			}

			m_image.PlotAxes( m_black, rangeX, rangeY, 16.0f, 0.1f );

			//////////////////////////////////////////////////////////////////////////
			// Render spectrum as (Real=Red, Imaginary=Blue) vertical lines for each frequency
			float2	cornerMin = m_image.RangedCoordinates2ImageCoordinates( rangeX, rangeY, new float2( rangeX.x, -1.0f ) );
			float2	cornerMax = m_image.RangedCoordinates2ImageCoordinates( rangeX, rangeY, new float2( rangeX.y, +1.0f ) );
			float2	delta = cornerMax - cornerMin;
			float	zeroY = cornerMin.y + 0.5f * delta.y;

			float2	Xr0 = new float2( 0, zeroY );
			float2	Xr1 = new float2( 0, 0 );
			float2	Xi0 = new float2( 0, zeroY );
			float2	Xi1 = new float2( 0, 0 );

			float	scale = 10.0f;

			float4	spectrumColorRe = new float4( 1, 0.25f, 0, 1 );
			float4	spectrumColorIm = new float4( 0, 0.5f, 1, 1 );
			int		size = m_spectrum.Length;
			int		halfSize = size >> 1;
			for ( int i=0; i < m_spectrum.Length; i++ ) {
				float	X = cornerMin.x + i * delta.x / m_spectrum.Length;
//				int		frequencyIndex = i;							// Show spectrum as output by FFT
				int		frequencyIndex = (i + halfSize) % size;		// Show offset spectrum with DC term in the middle
				Xr0.x = X;
				Xr1.x = X;
				Xr1.y = cornerMin.y + 0.5f * (scale * (float) m_spectrum[frequencyIndex].r + 1.0f) * delta.y;
				Xi0.x = X+1;
				Xi1.x = X+1;
				Xi1.y = cornerMin.y + 0.5f * (scale * (float) m_spectrum[frequencyIndex].i + 1.0f) * delta.y;

				m_image.DrawLine( spectrumColorRe, Xr0, Xr1 );
				m_image.DrawLine( spectrumColorIm, Xi0, Xi1 );
			}

			imagePanel.Bitmap = m_image.AsBitmap;
		}

		void	UpdateGraph1D_GPU( double _time ) {

			m_CB_Main.m._resolutionX = (uint) imagePanel.Width;
			m_CB_Main.m._resolutionY = (uint) imagePanel.Height;
			m_CB_Main.m._signalSize = (uint) SIGNAL_SIZE;
			m_CB_Main.m._signalFlags = (uint) m_signalType;
			m_CB_Main.m._signalFlags |= checkBoxShowInput.Checked ? 0x100U : 0;
			m_CB_Main.m._signalFlags |= checkBoxShowReconstructedSignal.Checked ? 0x200U : 0;
			m_CB_Main.m._time = (float) _time;

			m_device.SetRenderStates( RASTERIZER_STATE.CULL_NONE, DEPTHSTENCIL_STATE.DISABLED, BLEND_STATE.DISABLED );

			// Generate signal
			if ( m_Shader_GenerateSignal.Use() ) {
				m_device.SetRenderTarget( m_FFT1D_GPU.Input, null );
				m_CB_Main.UpdateData();
				m_device.RenderFullscreenQuad( m_Shader_GenerateSignal );
				m_device.RemoveRenderTargets();
			}

			// Apply FFT
			m_FFT1D_GPU.FFT_GPUInOut( -1.0f );

			// Copy spectrum & swap buffers
			m_texSpectrumCopy.CopyFrom( m_FFT1D_GPU.Output );
			m_FFT1D_GPU.SwapBuffers();

			// Apply FFT again to obtain signal again
			m_FFT1D_GPU.FFT_GPUInOut( 1.0f );

			// Display result
			if ( m_Shader_Display.Use() ) {
				m_device.SetRenderTarget( m_device.DefaultTarget, null );
				m_texSpectrumCopy.SetPS( 0 );
				m_FFT1D_GPU.Output.SetPS( 1 );

				m_CB_Main.UpdateData();

				m_device.RenderFullscreenQuad( m_Shader_Display );
				m_FFT1D_GPU.Output.RemoveFromLastAssignedSlots();
			}

			m_device.Present( false );
		}

		enum SIGNAL_TYPE {
			SQUARE,
			SINE,
			SAW,
			SINC,
			RANDOM,
		}
		SIGNAL_TYPE	m_signalType = SIGNAL_TYPE.SQUARE;

		enum FILTER_TYPE {
			NONE,
			CUT_LARGE,
			CUT_MEDIUM,
			CUT_SHORT,
			EXP,
			GAUSSIAN,
			INVERSE,
		}
		FILTER_TYPE	m_filter = FILTER_TYPE.NONE;

		delegate double	FilterDelegate( int i, int _frequency );

		Complex[]	m_spectrumGPU = new Complex[SIGNAL_SIZE];

		void	TestTransform1D( double _time ) {

			// Build the input signal
			Array.Clear( m_signalSource, 0, m_signalSource.Length );
			switch ( m_signalType ) {
				case SIGNAL_TYPE.SQUARE:
					for ( int i=0; i < SIGNAL_SIZE; i++ )
						m_signalSource[i].r = 0.5 * Math.Sin( _time ) + ((i + 50.0 * _time) % (SIGNAL_SIZE/2.0) < (SIGNAL_SIZE/4.0) ? 0.5 : -0.5);
					break;

				case SIGNAL_TYPE.SINE:
					for ( int i=0; i < SIGNAL_SIZE; i++ )
//						m_signalSource[i].r = Math.Cos( 2.0 * Math.PI * i / SIGNAL_SIZE + _time );
						m_signalSource[i].r = Math.Cos( (4.0 * (1.0 + Math.Sin( _time ))) * 2.0 * Math.PI * i / SIGNAL_SIZE );
					break;

				case SIGNAL_TYPE.SAW:
					for ( int i=0; i < SIGNAL_SIZE; i++ )
						m_signalSource[i].r = 0.5 * Math.Sin( _time ) + ((((i + 50.0 * _time) / 128.0) % 1.0) - 0.5);
					break;

				case SIGNAL_TYPE.SINC:
					for ( int i=0; i < SIGNAL_SIZE; i++ ) {
//						double	a = 4.0 * (1.0 + Math.Sin( _time )) * 2.0 * Math.PI * (1+i) / SIGNAL_SIZE;						// Asymmetrical
						double	a = 4.0 * (1.0 + Math.Sin( _time )) * 2.0 * Math.PI * (i-SIGNAL_SIZE/2.0) * 2.0 / SIGNAL_SIZE;	// Symmetrical
						m_signalSource[i].r = Math.Abs( a ) > 0.0 ? Math.Sin( a ) / a : 1.0;
					}
					break;

				case SIGNAL_TYPE.RANDOM:
					for ( int i=0; i < SIGNAL_SIZE; i++ )
						m_signalSource[i].r = SimpleRNG.GetUniform();
//						m_signalSource[i].r = SimpleRNG.GetExponential();
//						m_signalSource[i].r = SimpleRNG.GetBeta( 0.5, 1 );
//						m_signalSource[i].r = SimpleRNG.GetGamma( 1.0, 0.1 );
//						m_signalSource[i].r = SimpleRNG.GetCauchy( 0.0, 1.0 );
//						m_signalSource[i].r = SimpleRNG.GetChiSquare( 1.0 );
//						m_signalSource[i].r = SimpleRNG.GetNormal( 0.0, 0.1 );
//						m_signalSource[i].r = SimpleRNG.GetLaplace( 0.0, 0.1 );
//						m_signalSource[i].r = SimpleRNG.GetStudentT( 2.0 );
					break;
			}

			// Transform
			if ( m_FFTW_1D != null && checkBoxUseFFTW.Checked ) {
				m_FFTW_1D.FillInputSpatial( ( int x, int y, out float r, out float i ) => {
					r = (float) m_signalSource[x].r;
					i = (float) m_signalSource[x].i;
				} );
				m_FFTW_1D.Execute( fftwlib.FFT2D.Normalization.DIMENSIONS_PRODUCT );
				m_FFTW_1D.GetOutput( ( int x, int y, float r, float i ) => {
					m_spectrum[x].Set( r, i );
				} );
			} else {
//				DFT1D.DFT_Forward( m_signalSource, m_spectrum );
				FFT1D.FFT_Forward( m_signalSource, m_spectrum );
			}

			// Try the GPU version
			m_FFT1D_GPU.FFT_Forward( m_signalSource, m_spectrumGPU );
//			m_FFT1D_GPU.FFT_Forward( m_signalSource, m_spectrum );

double	sumSqDiffR = 0.0;
double	sumSqDiffI = 0.0;
for ( int i=0; i < m_spectrum.Length; i++ ) {
	Complex	diff = m_spectrum[i] - m_spectrumGPU[i];
	sumSqDiffR += diff.r*diff.r;
	sumSqDiffI += diff.i*diff.i;
}
labelDiff.Text = "SqDiff = " + sumSqDiffR.ToString( "G3" ) + " , " + sumSqDiffI.ToString( "G3" );
if ( m_FFTW_1D == null && checkBoxUseFFTW.Checked )
	labelDiff.Text += "\r\nERROR: Can't use FFTW because of an initialization error!";

if ( checkBoxInvertFilter.Checked )
	for ( int i=0; i < m_spectrum.Length; i++ )
		m_spectrum[i] = m_spectrumGPU[i];
// else
// 	for ( int i=0; i < m_spectrum.Length; i++ )
// 		m_spectrum[i] *= 2.0;


			// Filter
			FilterDelegate	filter = null;
			switch ( m_filter ) {
				case FILTER_TYPE.CUT_LARGE:
					filter = ( int i, int frequency ) => { return Math.Abs(frequency) > 256 ? 0 : 1.0; };			// Cut
					break;
				case FILTER_TYPE.CUT_MEDIUM:
					filter = ( int i, int frequency ) => { return Math.Abs(frequency) > 128 ? 0 : 1.0; };			// Cut
					break;
				case FILTER_TYPE.CUT_SHORT:
					filter = ( int i, int frequency ) => { return Math.Abs(frequency) > 64 ? 0 : 1.0; };			// Cut
					break;
				case FILTER_TYPE.EXP:
					filter = ( int i, int frequency ) => { return Math.Exp( -0.01f * Math.Abs(frequency) ); };		// Exponential
					break;
				case FILTER_TYPE.GAUSSIAN:
					filter = ( int i, int frequency ) => { return Math.Exp( -0.005f * frequency*frequency ); };		// Gaussian
					break;
				case FILTER_TYPE.INVERSE:
					filter = ( int i, int frequency ) => { return Math.Min( 1.0, 4.0 / (1+Math.Abs( frequency )) ); };	// Inverse
					break;
// 				case FILTER_TYPE.SINUS:
// 					filter = ( int i, int frequency ) => { return Math.Sin( -2.0f * Math.PI * frequency / 32 ); };		// Gni ?
// 					break;
			}
			if ( filter != null ) {
				int		size = m_spectrum.Length;
				int		halfSize = size >> 1;
				if ( !checkBoxInvertFilter.Checked ) {
					for ( int i=0; i < size; i++ ) {
						int		frequency = ((i + halfSize) % size) - halfSize;
						double	filterValue = filter( i, frequency );
 						m_spectrum[i] *= filterValue;
 					}
				} else {
					for ( int i=0; i < size; i++ ) {
						int		frequency = ((size - i) % size) - halfSize;
						double	filterValue = filter( i, frequency );
 						m_spectrum[i] *= filterValue;
 					}
				}
			}

			// Inverse Transform
			if ( m_FFTW_1D != null && checkBoxUseFFTW.Checked ) {
				m_FFTW_1D.FillInputFrequency( ( int x, int y, out float r, out float i ) => {
					r = (float) m_spectrum[x].r;
					i = (float) m_spectrum[x].i;
				} );
				m_FFTW_1D.Execute( fftwlib.FFT2D.Normalization.NONE );
				m_FFTW_1D.GetOutput( ( int x, int y, float r, float i ) => {
					m_signalReconstructed[x].Set( r, i );
				} );
			} else {
//				DFT1D.DFT_Inverse( m_spectrum, m_signalReconstructed );
				FFT1D.FFT_Inverse( m_spectrum, m_signalReconstructed );
			}
		}

		private void radioButtonSquare_CheckedChanged( object sender, EventArgs e ) {
			if ( radioButtonSquare.Checked )
				m_signalType = SIGNAL_TYPE.SQUARE;
			else if ( radioButtonSine.Checked )
				m_signalType = SIGNAL_TYPE.SINE;
			else if ( radioButtonTriangle.Checked )
				m_signalType = SIGNAL_TYPE.SAW;
			else if ( radioButtonSinc.Checked )
				m_signalType = SIGNAL_TYPE.SINC;
			else if ( radioButtonRandom.Checked )
				m_signalType = SIGNAL_TYPE.RANDOM;
		}

		private void radioButtonFilterNone_CheckedChanged( object sender, EventArgs e ) {
			if ( radioButtonFilterNone.Checked )
				m_filter = FILTER_TYPE.NONE;
			else if ( radioButtonFilterCutLarge.Checked )
				m_filter = FILTER_TYPE.CUT_LARGE;
			else if ( radioButtonFilterCutMedium.Checked )
				m_filter = FILTER_TYPE.CUT_MEDIUM;
			else if ( radioButtonFilterCutShort.Checked )
				m_filter = FILTER_TYPE.CUT_SHORT;
			else if ( radioButtonFilterExponential.Checked )
				m_filter = FILTER_TYPE.EXP;
			else if ( radioButtonFilterGaussian.Checked )
				m_filter = FILTER_TYPE.GAUSSIAN;
			else if ( radioButtonFilterInverse.Checked )
				m_filter = FILTER_TYPE.INVERSE;
		}

		#endregion

		#region FFT 2D Test

// 		Complex[][]		m_signalSource = new Complex[64][64];
// 		Complex[][]		m_spectrum = new Complex[64][64];
// 		Complex[][]		m_signalReconstructed = new Complex[64][64];

		void	UpdateGraph2D() {

			double	time = (DateTime.Now - m_startTime).TotalSeconds;

//			TestTransform2D( time );
		}

		#endregion

		private void buttonReload_Click( object sender, EventArgs e ) {
			m_device.ReloadModifiedShaders();
		}

		private void checkBoxGPU_CheckedChanged( object sender, EventArgs e ) {
			imagePanel.SkipPaint = checkBoxGPU.Checked;
		}
	}
}
