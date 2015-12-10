/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Structures, Variables & Buffers
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
#ifndef AUTOEXPOSURE_COMMON_INCLUDED
#define AUTOEXPOSURE_COMMON_INCLUDED

// Auto-exposure structure
struct autoExposure_t {
	float	EngineLuminanceFactor;		// The actual factor to apply to values stored to the HDR render target (it's simply LuminanceFactor * WORLD_TO_BISOU_LUMINANCE so it's a division by about 100)

	float	LuminanceFactor;			// The factor to apply to the HDR luminance to bring it to the LDR luminance (warning: still in world units, you must multiply by WORLD_TO_BISOU_LUMINANCE for a valid engine factor)
	float	MinLuminanceLDR;			// Minimum luminance (cd/m�) the screen will display as the value sRGB 1
	float	MaxLuminanceLDR;			// Maximum luminance (cd/m�) the screen will display as the value sRGB 255
	float	MiddleGreyLuminanceLDR;		// "Reference EV" luminance (cd/m�) the screen will display as the value sRGB 128 (55 linear)
	float	EV;							// Absolute Exposure Value of middle grey (sRGB 128) from a reference luminance of 0.15 cd/m� (see above for an explanation on that magic value)
	float	Fstop;						// The estimate F-stop number (overridden with env/autoexp/fstop_bias)

	uint	PeakHistogramValue;			// The maximum value found in the browsed histogram (values at start and end of histogram are not accounted for based on start & end bucket indices
};

StructuredBuffer<autoExposure_t>	_bufferAutoExposure : register(t0);	// The auto-exposure values for current frame
Texture2D<uint>						_texHistogram : register(t1);		// The image histogram (for debug purpose)


/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// General Helpers & Constants
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//

// The "arbitrary factor" that converts our lighting units to candelas so we can compute the histogram
// Actually, it's not _that_ arbitrary. It's computed assuming a 100W lightbulb emitting 1750 lumen (typical value)
//	will light a wall at 1 meter distance with an illuminance of 1750/(4PI) = 139.26 lm/m� (cd)
//
// For this we use the point light formula:
//	E = Phi / A = I . Omega / A = I . (A/r�) / A = I / r�
//
//	� Phi is the luminous flux in lumens (lm)
//	� I is the luminous intensity in lumens per steradian (lm/sr)
//	� A is the lit area in square meters (m�)
//	� Omega is the solid angle in steradians (sr) (which is also equal to the perceived area divided by the squared distance, hence the simplification using A/r�)
//
// Applying to a luminous intensity of 1750/(4PI) lm/sr and a distance of r=1 we get our magic number...
//
static const float	BISOU_TO_WORLD_LUMINANCE = 139.26;
static const float	WORLD_TO_BISOU_LUMINANCE = 1.0 / BISOU_TO_WORLD_LUMINANCE;


static const float	MIN_ADAPTABLE_SCENE_LUMINANCE = 1e-2;										// 0.001 cd/m� for star light but we limit to 0.01 cd/m� because we don't want to adapt that low!
static const float	MAX_ADAPTABLE_SCENE_LUMINANCE = 1e5;										// 100,000 cd/m� for the Sun
static const float	SCENE_LUMINANCE_RANGE_DB = 140.0;											// Scene dynamic range in decibels = 20.log10( MAX / MIN )
static const float	MIN_ADAPTABLE_SCENE_LUMINANCE_DB = -40;										// Minimum range in decibels
static const float	MAX_ADAPTABLE_SCENE_LUMINANCE_DB = 100;										// Maximum range in decibels

static const uint	TARGET_MONITOR_BITS_PRECISION = 8;											// Target monitors usually have 8 bits precision.
static const float	TARGET_MONITOR_LUMINANCE_RANGE = (1 << TARGET_MONITOR_BITS_PRECISION) - 1;	// So it has a range of 255 (the brightest pixel is 255 times brighter than the lowest)
static const float	TARGET_MONITOR_LUMINANCE_RANGE_DB = 48.164799306236991234198223155919;		// Target monitor's dynamic range in decibels = 20.log10( 1 << BITS )

static const uint	HISTOGRAM_SIZE = 128;														// We choose to have 128 buckets in our histogram
static const float	HISTOGRAM_BUCKET_RANGE_DB = SCENE_LUMINANCE_RANGE_DB / HISTOGRAM_SIZE;		// Range of a single histogram bucket (in dB)
static const float	TARGET_MONITOR_BUCKETS_COUNT = TARGET_MONITOR_LUMINANCE_RANGE_DB / HISTOGRAM_BUCKET_RANGE_DB;	// Amount of buckets covered by the target monitor range


// After measuring a luminance of 240cd/m� on a typical display device (DELL U2412M) using the i1 Display Pro probe and taking pictures
//	of a white screen using my Canon EOS 300D, the EOS told me that what it considered to be "EV 0" was met for F=4 and t=1/100s.
//
// According to wikipedia (http://en.wikipedia.org/wiki/Exposure_value):
//	EV = log2( F� / t )
//
//		F is the F-stop number
//		t is the exposure time
// 
// This gives us 0 EV at 240 cd/m� = log2( 4*4 / (1/100) ) = log2( 1600 )
//
// This tells us that the absolute reference luminance for the EOS should be ~ 240 / 1600 = 0.15 cd/m�
// This reference luminance will map to sRGB = 0.5 (i.e. 128) and will be considered to be the "EV absolute 0"
//
static const float	ABSOLUTE_EV_REFERENCE_LUMINANCE = 0.15;

// Converts a luminance to a decibel value
//	dB = 20 * log10( Luminance )
float	Luminance2dB( float _Luminance ) {
	return 8.6858896380650365530225783783321 * log( _Luminance );
}

// Converts a luminance to a decibel value
//	Luminance = 10^(dB / 20.0)
float	dB2Luminance( float _dB ) {
	return pow( 10.0, 0.05 * _dB );
}


// These are constants controling the "sticky integral"
// The idea is simply to give less weight to luminances that are too far from the currently adapted luminance
// The weight is controled by a smooth curve going from 1 to LowValue in N buckets
// This way, the integral is less likely to move away from current adapted value because of slightly equal integral values:
//	When there's about an equal amount of dark and bright pixels, the integral can flip from dark to bright adaptation easily
//	but by giving less weights to luminances too far away from current luminances, we're increasing the luminance
//	discrepancy required to trigger the flip...
//
static const float	WEIGHT_FADE_BUCKET_BIAS = -0.0;		// Weight is at its maximum at maximum luminance minus this bias (in buckets)
static const float	WEIGHT_FADE_BUCKETS_COUNT = 20.0;	// Amount of buckets away from goal to reach the minimum weight
static const float	WEIGHT_FADE_LOW_VALUE = 0.4;		// Minimum weight value for buckets too far away
//static const float	WEIGHT_FADE_LOW_VALUE = 1.0;		// <== Uncomment this to disable sticky integral mode

float	ComputeStickyIntegralTargetBucket( autoExposure_t _LastFrameResult ) {
	return (Luminance2dB( _LastFrameResult.MaxLuminanceLDR ) - MIN_ADAPTABLE_SCENE_LUMINANCE_DB) / HISTOGRAM_BUCKET_RANGE_DB	// <== This gives the index of the max currently adapted bucket
			+ WEIGHT_FADE_BUCKET_BIAS * TARGET_MONITOR_BUCKETS_COUNT;															// <== That we offset with a specific bias
}

// _BucketDelta = distance between currently measured bucket and previously adapted target bucket
float	ComputeStickyIntegralWeight( float _BucketDelta ) {
	return lerp( WEIGHT_FADE_LOW_VALUE, 1.0, smoothstep( WEIGHT_FADE_BUCKETS_COUNT, 0.0, abs(_BucketDelta) ) );
}

// Computes the exposure factor based on the user's provided EV
// float	ComputeExposureFactor() {
// 	return exp2( -($(PostFX/Luminance/customEVBias).x + $(env/autoexp/EV)) );
// }

// Reads back the current luminance exposure data for the current frame
autoExposure_t	ReadAutoExposureParameters() {
	return _bufferAutoExposure[0];
}

#endif // #ifndef AUTOEXPOSURE_COMMON_INCLUDED
