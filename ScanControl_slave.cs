﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KeysightSD1;
using System.Windows.Forms;


// 6/20/18 start working on scan control API for DE in slave mode

namespace ScanControl_slave
{
    public enum HW_STATUS_RETURNS
    {
        HW_SUCCESS,
        HW_OTHER
    }


    public class ScanControl_cz
    {
        private List<double> xpoints;
        private List<double> ypoints;
        private List<int> xindex;
        private List<int> yindex;

        public HW_STATUS_RETURNS CancelScan()
        {
            int status;
            // Channel 1 for y scan and channel 2 for x scan

            //Create an instance of the AOU module
            SD_AOU moduleAOU = new SD_AOU();
            string ModuleName = "M3201A";
            int nChassis = 1;
            int nSlot = 7;

            if ((status = moduleAOU.open(ModuleName, nChassis, nSlot)) < 0)
            {
                Console.WriteLine("Error openning the Module 'M3201A', make sure the slot and chassis are correct. Aborting..." + status);
                Console.ReadKey();

                return HW_STATUS_RETURNS.HW_SUCCESS;
            }
            status = moduleAOU.AWGflush(1);
            status = moduleAOU.AWGflush(2);
            status = moduleAOU.AWGflush(3);
            status = moduleAOU.AWGflush(4);

            return HW_STATUS_RETURNS.HW_SUCCESS;

        }

        public HW_STATUS_RETURNS ScanControlInitialize(double x_amp, double y_amp, double[] Xarray_vol, double[] Yarray_vol, int[] Xarray_index, int[] Yarray_index, double delay, int recording_rate, int Option2D, int Nmultiframes)
        {
            int status;
            // Channel 1 for y scan and channel 2 for x scan

            //Create an instance of the AOU module
            SD_AOU moduleAOU = new SD_AOU();
            string ModuleName = "M3201A";
            int nChassis = 1;
            int nSlot = 7;

            if ((status = moduleAOU.open(ModuleName, nChassis, nSlot)) < 0)
            {
                Console.WriteLine("Error openning the Module 'M3201A', make sure the slot and chassis are correct. Aborting...");
                Console.ReadKey();

                return HW_STATUS_RETURNS.HW_SUCCESS;
            }

            // For global shutter mode, set targer trigger delay time in ns, TriggerDelay and TriggerDelayCeil set the acceptable range of dealy
            int TriggerDelay = 0;
            int TriggerDelayCeil = 203900; // Define maximum triggger delay, otherwise the software would likely to use max Prescaling factor to satisfy the delay time

            // Determine prescaling factor and number of samples per step to use
            // According to Benjamin Bammels suggestion, use 5% to 10% longer frame time on AWG compared to DE frame integration time

            int nSamples;
            int Prescaling;

            nSamples = (int)Math.Ceiling(1.05e8 / recording_rate / 4095);
            Prescaling = (int)Math.Ceiling(1.05e8 / recording_rate / nSamples);
            while (Prescaling > 1.10e8 / recording_rate / nSamples || nSamples == 1 || TriggerDelay % (10 * Prescaling) > 1)
            {
                nSamples++;
                Prescaling = (int)Math.Ceiling(1.05e8 / recording_rate / nSamples);
            }

            int SampleDelay;
            SampleDelay = (int)Math.Ceiling((double)TriggerDelay / 10 / (double)Prescaling);

            Console.WriteLine("Precaling factor " + Prescaling + " will be used with " + nSamples + " for each beam position.");
            Console.WriteLine("Scan delayed by " + (int)SampleDelay * Prescaling * 10 + " ns from beam position movement.");
            Console.WriteLine("Sample delayed by + " + (int)SampleDelay);

            // Config amplitude and setup AWG in channels 1 and 2,
            moduleAOU.channelAmplitude(1, y_amp);
            moduleAOU.channelWaveShape(1, SD_Waveshapes.AOU_AWG);
            moduleAOU.channelAmplitude(2, x_amp);
            moduleAOU.channelWaveShape(2, SD_Waveshapes.AOU_AWG);
            moduleAOU.channelAmplitude(3, 0.5);
            moduleAOU.channelWaveShape(3, SD_Waveshapes.AOU_AWG);
            moduleAOU.channelAmplitude(4, 0.5);
            moduleAOU.channelWaveShape(4, SD_Waveshapes.AOU_AWG);
            moduleAOU.waveformFlush();

            // Convert array into list

            xpoints = new List<double>();
            ypoints = new List<double>();
            xindex = new List<int>();
            yindex = new List<int>();

            xpoints.Clear();
            ypoints.Clear();
            xindex.Clear();
            yindex.Clear();

            xpoints = Xarray_vol.ToList();
            ypoints = Yarray_vol.ToList();
            xindex = Xarray_index.ToList();
            yindex = Yarray_index.ToList();

            status = moduleAOU.AWGflush(1);
            Console.WriteLine("Status for channel 1 " + status);
            status = moduleAOU.AWGflush(2);
            Console.WriteLine("Status for channel 2 " + status);
            status = moduleAOU.AWGflush(3);
            Console.WriteLine("Status for channel 3 " + status);
            status = moduleAOU.AWGflush(4);
            Console.WriteLine("Status for channel 4 " + status);



            #region X scan generation

            // Generate and queue waveform for X channel on waveform #0 (channel 2)
            // Currently when the waveform is full, break all loops using break, a better way would be to put the waveform get into a function and use return
            // to break all the loops.

            var Waveform_X = new double[nSamples * xindex.Count()];
            int Count = 0;
            // Start with loop for delay cycle
            for (int i = 0; i < SampleDelay; i++)
            {
                Waveform_X[Count] = -0.5; //Start with beam outside scan region, scan region ranges from -0.5 to 0.5
                Count++;    // Count represents the current number of points in waveform
            }
            // create double array for each x cycle
            for (int ix = 0; ix < xindex.Count; ix++)
            {
                for (int i = 0; i < nSamples; i++)
                {
                    Waveform_X[Count] = xpoints[xindex[xindex.Count - ix - 1]];
                    Count++;
                    if (Count == nSamples * xindex.Count())
                    {
                        break;  // End waveform generation when the waveform is full
                    }
                }
                if (Count == nSamples * xindex.Count())
                {
                    break;  // Also break the outer loop, in case the delay length is more than one beam position
                }
            }

            int length_x;
            length_x = Waveform_X.Length;


            #endregion

            #region Y scan generation

            //Set spectial nSamplesY and prescalingY because its variation frequency is much lower.   
            int nSamplesY;
            int PrescalingY;

            nSamplesY = (int)Math.Ceiling(1.05e8 / recording_rate * xindex.Count() / 4095);
            PrescalingY = (int)Math.Ceiling(1.05e8 / recording_rate * xindex.Count() / nSamplesY);
            while (PrescalingY > 1.10e8 / (recording_rate / xindex.Count()) / nSamplesY || nSamplesY == 1 || TriggerDelay % (10 * PrescalingY) > 1)
            {
                nSamplesY++;
                PrescalingY = (int)Math.Ceiling(1.05e8 / (recording_rate / xindex.Count()) / nSamplesY);
            }

            SampleDelay = (int)Math.Ceiling((double)TriggerDelay / 10 / (double)Prescaling);
            nSamplesY = (int)Math.Ceiling((double)nSamplesY / xindex.Count()); // get back the nSamples for each position 
            Console.WriteLine("Precaling factor for y scan" + PrescalingY + " will be used with " + nSamplesY + " for each beam position.");
            Console.WriteLine("Scan delayed by " + (int)SampleDelay * PrescalingY * 10 + " ns from beam position movement.");
            Console.WriteLine("Sample delayed by + " + (int)SampleDelay);
            // Generate and queue waveform for Y channel on waveform #1 (channel 1)

            var Waveform_Y = new double[nSamplesY * xindex.Count() * yindex.Count()];
            Count = 0;
            // Start with loop for delay cycle
            for (int i = 0; i < SampleDelay; i++)
            {
                Waveform_Y[Count] = -1; //Start with beam outside scan region, scan region ranges from -0.5 to 0.5
                Count++;
            }

            for (int iy = 0; iy < yindex.Count(); iy++)
            {
                for (int ix = 0; ix < xindex.Count(); ix++)
                {
                    for (int i = 0; i < nSamplesY; i++)
                    {
                        Waveform_Y[Count] = ypoints[yindex[yindex.Count - iy - 1]];
                        Count++;
                        if (Count == nSamplesY * xindex.Count() * yindex.Count())
                        {
                            break;  // End waveform generation when the waveform is full
                        }
                    }
                    if (Count == nSamplesY * xindex.Count() * yindex.Count())
                    {
                        break;  // Also break outer loop
                    }
                }
                if (Count == nSamplesY * xindex.Count() * yindex.Count())
                {
                    break;  // Break outmost loop
                }
            }
            int length_y;
            length_y = Waveform_Y.Length;
           
            #endregion            

            #region generate DE trigger

            // Generate and queue waveform for DE trigger on wavefrom #2 (channel 3), same size and reps as x array

            var Waveform_DE = new double[nSamples * xindex.Count()];
            for (int ix = 0; ix < xindex.Count; ix++)
            {
                Waveform_DE[ix * nSamples] = -1;
            }
            int length_DE;
            length_DE = Waveform_DE.Length;

            #endregion

            #region generate digitizer trigger

            // Generate and queue waveform for digitizer trigger on waveform #3 (channel 4) 
            // trigger signal same size as x array, run only once

            var Waveform_DIGI = new double[nSamples * xindex.Count()];
            for (int ix = 0; ix < nSamples; ix++)
            {
                Waveform_DIGI[ix + SampleDelay] = -1; // set first nSamples after SampleDelay points to -1 to create on single trigger
            }
            int length_DIGI;
            length_DIGI = Waveform_DIGI.Length;
            
            #endregion

            #region check wave_array sized and load all the four waveforms

            double memorySizeMB = (length_x + length_y + length_DE + length_DIGI) * 8e-6;
            if (memorySizeMB < 2000)
            {
                Console.WriteLine("The total memory size of the four waveform_array is" + memorySizeMB + " MB");
            }
            else
            {
                Console.WriteLine("The total memory size of the four waveform_array is" + memorySizeMB + " MB");
                System.Windows.Forms.MessageBox.Show("Your settings exceed RAM Limitation! ", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return HW_STATUS_RETURNS.HW_OTHER;
            }

            // x scan
            var SD_Waveform_X = new SD_Wave(SD_WaveformTypes.WAVE_ANALOG, Waveform_X);
            status = moduleAOU.waveformLoad(SD_Waveform_X, 0, 1);  // padding option 1 is used to maintain ending voltage after each WaveForm
            if (status < 0)
            {
                Console.WriteLine("Error while loading x waveform");
            }
            Console.WriteLine("X waveform size " + (double)moduleAOU.waveformGetMemorySize(0) / 1000000 + " MB");
            // queue waveform into channel 1 and loop for yindex.count() times
            status = moduleAOU.AWGqueueWaveform(1, 0, SD_TriggerModes.AUTOTRIG, 0, yindex.Count() * Nmultiframes, Prescaling);
            if (status < 0)
            {
                Console.WriteLine("Error while queuing x waveform");
            }

            //y scan
            var SD_Waveform_Y = new SD_Wave(SD_WaveformTypes.WAVE_ANALOG, Waveform_Y);
            status = moduleAOU.waveformLoad(SD_Waveform_Y, 1, 1);       // padding option 1 is used to maintain ending voltage after each WaveForm
            if (status < 0)
            {
                Console.WriteLine("Error while loading y waveform");
            }
            Console.WriteLine("Y waveform size " + (double)moduleAOU.waveformGetMemorySize(1) / 1000000 + " MB");
            // queue waveform into channel 2 and run once
            status = moduleAOU.AWGqueueWaveform(2, 1, SD_TriggerModes.AUTOTRIG, 0, Nmultiframes, Prescaling);
            if (status < 0)
            {
                Console.WriteLine("Error while queuing y waveform");
            }

            //DE camera
            var SD_Waveform_DE = new SD_Wave(SD_WaveformTypes.WAVE_ANALOG, Waveform_DE);
            status = moduleAOU.waveformLoad(SD_Waveform_DE, 2, 1);       // padding option 1 is used to maintain ending voltage after each WaveForm
            if (status < 0)
            {
                Console.WriteLine("Error while loading x waveform");
            }
            status = moduleAOU.AWGqueueWaveform(3, 2, SD_TriggerModes.AUTOTRIG, 0, yindex.Count() * Nmultiframes, Prescaling);
            Console.WriteLine("Trigger waveform size " + (double)moduleAOU.waveformGetMemorySize(2) / 1000000 + " MB");
            if (status < 0)
            {
                Console.WriteLine("Error while queuing camera trigger, error code " + status);
            }

            // Digitizer
            var SD_Waveform_DIGI = new SD_Wave(SD_WaveformTypes.WAVE_ANALOG, Waveform_DIGI);
            status = moduleAOU.waveformLoad(SD_Waveform_DIGI, 3, 1);       // padding option 1 is used to maintain ending voltage after each WaveForm
            if (status < 0)
            {
                Console.WriteLine("Error while loading x waveform");
            }
            status = moduleAOU.AWGqueueWaveform(4, 3, SD_TriggerModes.AUTOTRIG, 0, Nmultiframes, Prescaling);
            Console.WriteLine("Trigger waveform size " + (double)moduleAOU.waveformGetMemorySize(3) / 1000000 + " MB");
            if (status < 0)
            {
                Console.WriteLine("Error while queuing digitizer trigger, error code " + status);
            }

            #endregion

            // Configure all channels to single shot, X and trigger will automatically stop after certain amount of cycles
            moduleAOU.AWGqueueConfig(1, 0);
            moduleAOU.AWGqueueConfig(2, 0);
            moduleAOU.AWGqueueConfig(3, 0); // Should also be 0 here?
            moduleAOU.AWGqueueConfig(4, 0);

            // Start both channel and wait for triggers, start channel 0,1,2: 00000111 = 7; start channel 0,1,2,3: 00001111 = 15
            System.Threading.Thread.Sleep(1000);
            if (Option2D == 0)
                moduleAOU.AWGstartMultiple(15);
            else
                moduleAOU.AWGstartMultiple(11); // don't start channel 3 for DE trigger if doing 2D scan mode
            //moduleAOU.AWGstartMultiple(3);


            return HW_STATUS_RETURNS.HW_SUCCESS;

        }

    }
}