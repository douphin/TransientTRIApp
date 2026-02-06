using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

                SendCommand("TR SC");
                Console.WriteLine("Sent: TR SC");

                SendCommand("TR EP");
                Console.WriteLine("Sent: TR EP");

                // Configure Trigger Rate
                Console.WriteLine("\n--- Configuring Trigger Rate ---");
                string trInCommand = string.Format("TR IN {0:e4} HZ;", triggerRateHz);
                SendCommand(trInCommand);
                Console.WriteLine($"Sent: {trInCommand}");

                string trInRead = SendAndRead("TR IN");
                Console.WriteLine($"Read back: {trInRead}");
                ValidateSetting("Trigger Rate", triggerRateHz, trInRead);

                // Configure Pulse Width
                Console.WriteLine("\n--- Configuring Pulse Width ---");
                string tiWdCommand = string.Format("TI WD {0:e4} s;", pulseWidthSec);
                SendCommand(tiWdCommand);
                Console.WriteLine($"Sent: {tiWdCommand}");

                string tiWdRead = SendAndRead("TI WD");
                Console.WriteLine($"Read back: {tiWdRead}");
                ValidateSetting("Pulse Width", pulseWidthSec, tiWdRead);

                // Configure LV Peak
                Console.WriteLine("\n--- Configuring LV Peak ---");
                string lvPkCommand = string.Format("LV PK {0:e4} V;", lvPeakV);
                SendCommand(lvPkCommand);
                Console.WriteLine($"Sent: {lvPkCommand}");

                string lvPkRead = SendAndRead("LV PK");
                Console.WriteLine($"Read back: {lvPkRead}");
                ValidateSetting("LV Peak", lvPeakV, lvPkRead);

                Console.WriteLine("\n✓ Pulse generator configured successfully!");
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
                string trInRead = SendAndRead("TR IN");
                if (double.TryParse(ExtractValue(trInRead), out double triggerRate))
                    settings["TriggerRateHz"] = triggerRate;

                string tiWdRead = SendAndRead("TI WD");
                if (double.TryParse(ExtractValue(tiWdRead), out double pulseWidth))
                    settings["PulseWidthSec"] = pulseWidth;

                string lvPkRead = SendAndRead("LV PK");
                if (double.TryParse(ExtractValue(lvPkRead), out double lvPeak))
                    settings["LVPeakV"] = lvPeak;

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
                double readbackValue = double.Parse(ExtractValue(readbackString));
                double tolerance = Math.Abs(expectedValue * 0.001); // 0.1% tolerance

                if (Math.Abs(readbackValue - expectedValue) <= tolerance)
                {
                    Console.WriteLine($"✓ {settingName} validated: {readbackValue:e4}");
                }
                else
                {
                    Console.WriteLine($"⚠ {settingName} mismatch: expected {expectedValue:e4}, got {readbackValue:e4}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Could not validate {settingName}: {ex.Message}");
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
    }
}