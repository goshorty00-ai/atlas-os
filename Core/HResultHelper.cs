using System;
using System.Collections.Generic;

namespace AtlasAI.Core
{
    /// <summary>
    /// Helper class for mapping HRESULT codes to human-readable descriptions
    /// </summary>
    public static class HResultHelper
    {
        private static readonly Dictionary<int, string> KnownHResults = new Dictionary<int, string>
        {
            // Media Foundation errors
            { unchecked((int)0xC00D11B1), "MF_E_NO_AUDIO_PLAYBACK_DEVICE - No audio playback device available" },
            { unchecked((int)0xC00D36B4), "MF_E_AUDIO_RECORDING_DEVICE_IN_USE - Audio recording device is in use" },
            { unchecked((int)0xC00D36E6), "MF_E_AUDIO_RECORDING_DEVICE_INVALIDATED - Audio recording device was removed" },
            { unchecked((int)0xC00D4E86), "MF_E_TRANSFORM_TYPE_NOT_SET - Transform type not set" },
            { unchecked((int)0xC00D36B3), "MF_E_INVALIDMEDIATYPE - Invalid media type" },
            
            // Speech Recognition errors
            { unchecked((int)0x80045509), "SPERR_NOT_FOUND - Speech recognition engine not found" },
            { unchecked((int)0x8004503A), "SPERR_DEVICE_BUSY - Speech recognition device is busy" },
            { unchecked((int)0x80045003), "SPERR_UNINITIALIZED - Speech recognition not initialized" },
            
            // Audio errors
            { unchecked((int)0x88890008), "AUDCLNT_E_DEVICE_IN_USE - Audio device is in use" },
            { unchecked((int)0x88890004), "AUDCLNT_E_DEVICE_INVALIDATED - Audio device was removed" },
            { unchecked((int)0x8889000A), "AUDCLNT_E_UNSUPPORTED_FORMAT - Audio format not supported" },
            
            // COM errors
            { unchecked((int)0x80004005), "E_FAIL - Unspecified error" },
            { unchecked((int)0x80070057), "E_INVALIDARG - Invalid argument" },
            { unchecked((int)0x8007000E), "E_OUTOFMEMORY - Out of memory" },
            { unchecked((int)0x80004001), "E_NOTIMPL - Not implemented" },
        };

        /// <summary>
        /// Get a human-readable description for an HRESULT code
        /// </summary>
        public static string GetDescription(int hresult)
        {
            if (KnownHResults.TryGetValue(hresult, out var description))
            {
                return description;
            }
            return $"Unknown HRESULT: 0x{hresult:X8}";
        }

        /// <summary>
        /// Format an HRESULT with its description
        /// </summary>
        public static string FormatHResult(int hresult)
        {
            return $"0x{hresult:X8} - {GetDescription(hresult)}";
        }

        /// <summary>
        /// Extract HRESULT from exception and return formatted string
        /// </summary>
        public static string ExtractAndFormat(Exception ex)
        {
            if (ex is System.Runtime.InteropServices.COMException comEx)
            {
                return FormatHResult(comEx.HResult);
            }
            else if (ex.HResult != 0)
            {
                return FormatHResult(ex.HResult);
            }
            return "No HRESULT available";
        }
    }
}
