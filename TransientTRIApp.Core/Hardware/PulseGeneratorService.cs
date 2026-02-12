using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ivi.Visa;
using NationalInstruments.Visa;
using TransientTRIApp.Common.Interfaces;

namespace TransientTRIApp.Core.Hardware
{
    public class PulseGeneratorService : IPulseGeneratorService, IDisposable
    {
        private IMessageBasedSession _session;
        private bool _connected = false;
        private int _commandDelayMs = 300;

        public void Connect(string gpibAddress)
        {
            try
            {
                // Create a VISA resource manager and open session
                var rm = new ResourceManager();
                _session = (IMessageBasedSession)rm.Open(gpibAddress, AccessModes.None, 5000);

                // Set longer timeout for reads/writes
                _session.TimeoutMilliseconds = 5000;

                _connected = true;
                Console.WriteLine($"Connected to pulse generator at {gpibAddress}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to pulse generator: {ex.Message}");
                throw;
            }
        }

        public void Configure(double triggerRateHz, double pulseWidthSec, double lvPeakV)
        {
            if (!_connected)
                throw new InvalidOperationException("Pulse generator not connected. Call Connect() first.");

            try
            {
                // Send initial configuration commands
                SendCommand("MO PL");
                Console.WriteLine("Sent: MO PL");
                Thread.Sleep(_commandDelayMs); 

                SendCommand("TR SC");
                Console.WriteLine("Sent: TR SC");
                Thread.Sleep(_commandDelayMs); 

                SendCommand("TR EP");
                Console.WriteLine("Sent: TR EP");
                Thread.Sleep(_commandDelayMs); 

                // Configure Trigger Rate
                Console.WriteLine("\n--- Configuring Trigger Rate ---");
                string trInCommand = string.Format("TR IN {0:e4} HZ;", triggerRateHz);
                SendCommand(trInCommand);
                Console.WriteLine($"Sent: {trInCommand}");
                Thread.Sleep(_commandDelayMs); 

                string trInRead = SendAndRead("TR IN");
                Console.WriteLine($"Read back: {trInRead}");
                ValidateSetting("Trigger Rate", triggerRateHz, trInRead);
                Thread.Sleep(_commandDelayMs); 

                // Configure Pulse Width
                Console.WriteLine("\n--- Configuring Pulse Width ---");
                string tiWdCommand = string.Format("TI WD {0:e4} s;", pulseWidthSec);
                SendCommand(tiWdCommand);
                Console.WriteLine($"Sent: {tiWdCommand}");
                Thread.Sleep(_commandDelayMs); 

                string tiWdRead = SendAndRead("TI WD");
                Console.WriteLine($"Read back: {tiWdRead}");
                ValidateSetting("Pulse Width", pulseWidthSec, tiWdRead);
                Thread.Sleep(_commandDelayMs); 

                // Configure LV Peak
                Console.WriteLine("\n--- Configuring LV Peak ---");
                string lvPkCommand = string.Format("LV PK {0:e4} V;", lvPeakV);
                SendCommand(lvPkCommand);
                Console.WriteLine($"Sent: {lvPkCommand}");
                Thread.Sleep(_commandDelayMs+100); 

                string lvPkRead = SendAndRead("LV PK");
                Console.WriteLine($"Read back: {lvPkRead}");
                ValidateSetting("LV Peak", lvPeakV, lvPkRead);
                //Thread.Sleep(_commandDelayMs); 

                Console.WriteLine("\nPulse generator configured successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configuring pulse generator: {ex.Message}");
                throw;
            }
        }

        public Dictionary<string, double> GetCurrentSettings()
        {
            if (!_connected)
                throw new InvalidOperationException("Pulse generator not connected.");

            var settings = new Dictionary<string, double>();

            try
            {
                settings["TriggerRateHz"] = ExtractValueWithUnitConversion(SendAndRead("TR IN"));
                Thread.Sleep(_commandDelayMs);
                settings["PulseWidthSec"] = ExtractValueWithUnitConversion(SendAndRead("TI WD"));
                Thread.Sleep(_commandDelayMs);
                settings["LVPeakV"] = ExtractValueWithUnitConversion(SendAndRead("LV PK"));


                //string trInRead = SendAndRead("TR IN");
                //if (double.TryParse(ExtractValue(trInRead), out double triggerRate))
                //    settings["TriggerRateHz"] = triggerRate;
                //Thread.Sleep(_commandDelayMs);

                //string tiWdRead = SendAndRead("TI WD");
                //if (double.TryParse(ExtractValue(tiWdRead), out double pulseWidth))
                //    settings["PulseWidthSec"] = pulseWidth;
                //Thread.Sleep(_commandDelayMs);

                //string lvPkRead = SendAndRead("LV PK");
                //if (double.TryParse(ExtractValue(lvPkRead), out double lvPeak))
                //    settings["LVPeakV"] = lvPeak;

                return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading settings: {ex.Message}");
                return settings;
            }
        }

        public void Disconnect()
        {
            if (_session != null)
            {
                try
                {
                    _session.Dispose();
                    _connected = false;
                    Console.WriteLine("Disconnected from pulse generator");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disconnecting: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        // --- Private Helper Methods ---

        private void SendCommand(string command)
        {
            try
            {
                byte[] commandBytes = Encoding.ASCII.GetBytes(command + "\n");
                _session.RawIO.Write(commandBytes);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send command '{command}': {ex.Message}");
            }
        }

        private string SendAndRead(string query)
        {
            try
            {
                // Send query
                byte[] queryBytes = Encoding.ASCII.GetBytes(query + "\n");
                _session.RawIO.Write(queryBytes);

                // Wait for device to process and prepare response
                System.Threading.Thread.Sleep(500);

                // Read response
                byte[] responseBytes = _session.RawIO.Read(256); // Read up to 256 bytes
                string response = Encoding.ASCII.GetString(responseBytes).Trim();
                Console.WriteLine($"Raw response: {response} (bytes: {BitConverter.ToString(responseBytes, 0, Math.Min(responseBytes.Length, 30))})");
                return response;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send query '{query}': {ex.Message}");
            }
        }

        private void ValidateSetting(string settingName, double expectedValue, string readbackString)
        {
            try
            {
                double readbackValue = ExtractValueWithUnitConversion(readbackString);
                double tolerance = Math.Abs(expectedValue * 0.001); // 0.1% tolerance

                if (Math.Abs(readbackValue - expectedValue) <= tolerance)
                {
                    Console.WriteLine($"{settingName} validated: {readbackValue:e4}");
                }
                else
                {
                    Console.WriteLine($"{settingName} mismatch: expected {expectedValue:e4}, got {readbackValue:e4}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not validate {settingName}: {ex.Message}");
            }
        }

        private string ExtractValue(string response)
        {
            // Extract numeric value from response
            // Response format is typically "TR IN 1.0000e+03" or similar
            string[] parts = response.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0)
            {
                // Return the last part which should be the numeric value
                return parts[parts.Length - 1];
            }

            throw new Exception($"Could not extract value from response: {response}");
        }

        private double ExtractValueWithUnitConversion(string response)
        {
            try
            {
                string[] parts = response.Split(new[] { ' ', '\t', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);

                double numericValue = 0;
                string unit = "";

                // Find the numeric part and the unit
                foreach (string part in parts)
                {
                    // Try to parse including scientific notation (e.g., 1e-3, 1.5E+02)
                    if (double.TryParse(part, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        numericValue = value;
                    }
                    else if (!string.IsNullOrEmpty(part) && char.IsLetter(part[0]))
                    {
                        unit = part.ToUpper();
                    }
                }

                // Convert based on unit type
                // Frequency units (to Hz)
                if (unit == "KHZ")
                    numericValue *= 1000;
                else if (unit == "MHZ")
                    numericValue *= 1000000;
                else if (unit == "GHZ")
                    numericValue *= 1000000000;

                // Time units (to seconds)
                else if (unit == "MS" || unit == "MSEC")
                    numericValue /= 1000;
                else if (unit == "US" || unit == "USEC")
                    numericValue /= 1000000;
                else if (unit == "NS" || unit == "NSEC")
                    numericValue /= 1000000000;

                // Voltage units (to volts)
                else if (unit == "MV" || unit == "MVOLT")
                    numericValue /= 1000;
                else if (unit == "UV" || unit == "UVOLT")
                    numericValue /= 1000000;

                // Current units (to amps)
                else if (unit == "MA" || unit == "MAMP")
                    numericValue /= 1000;
                else if (unit == "UA" || unit == "UAMP")
                    numericValue /= 1000000;
                else if (unit == "NA" || unit == "NAMP")
                    numericValue /= 1000000000;

                // Power units (to watts)
                else if (unit == "MW" || unit == "MWATT")
                    numericValue /= 1000;
                else if (unit == "UW" || unit == "UWATT")
                    numericValue /= 1000000;

                Console.WriteLine($"Extracted: {numericValue} (unit was: {unit})");
                return numericValue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting value: {ex.Message}");
                return 0;
            }
        }

        private string ExtractValueWithoutUnitConversion(string response)
        {
            try
            {
                string[] parts = response.Split(new[] { ' ', '\t', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);

                double numericValue = 0;
                string unit = "";

                // Find the numeric part and the unit
                foreach (string part in parts)
                {
                    // Try to parse including scientific notation (e.g., 1e-3, 1.5E+02)
                    if (double.TryParse(part, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        numericValue = value;
                    }
                    else if (!string.IsNullOrEmpty(part) && char.IsLetter(part[0]))
                    {
                        unit = part.ToUpper();
                    }
                }

                Console.WriteLine($"Extracted: {numericValue} (unit was: {unit})");
                return numericValue.ToString() + " " + unit;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting value: {ex.Message}");
                return "Unknown";
            }
        }
    }
}