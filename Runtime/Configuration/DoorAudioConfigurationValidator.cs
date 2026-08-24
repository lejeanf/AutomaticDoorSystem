using System.Collections.Generic;
using jeanf.audiosystems;
using UnityEngine;

namespace AutomaticDoorSystem
{
    /// <summary>
    /// Edit-time checks for DoorAudioConfiguration assets, mirroring exactly what DoorAudioBridge
    /// does at runtime: the SamplerData list is tried first and the legacy clip list is the
    /// fallback, per event, with a random pick inside whichever list answers. Anything flagged
    /// here is therefore a sound that will be missing, silent or inconsistent in playmode.
    /// Shared by the config's inspector, the Setup Validator window and the editor tests.
    /// </summary>
    public static class DoorAudioConfigurationValidator
    {
        public readonly struct Issue
        {
            public readonly bool IsError;
            public readonly string Message;
            /// <summary>The sub-object the issue is about (a referenced SamplerData), when there is one.</summary>
            public readonly Object Context;

            public Issue(bool isError, string message, Object context = null)
            {
                IsError = isError;
                Message = message;
                Context = context;
            }
        }

        public static List<Issue> Validate(DoorAudioConfiguration config)
        {
            var issues = new List<Issue>();

            if (config == null)
            {
                issues.Add(new Issue(true, "DoorAudioConfiguration reference is null."));
                return issues;
            }

            if (config.volume <= 0f)
            {
                issues.Add(new Issue(false,
                    "volume is 0 - every door sound from this config plays inaudibly."));
            }

            if (config.maxDistance <= config.minDistance)
            {
                issues.Add(new Issue(false,
                    $"maxDistance ({config.maxDistance}) is not greater than minDistance ({config.minDistance}) - " +
                    "3D rolloff is degenerate and audibility becomes unpredictable."));
            }

            bool openPlays = CheckEvent(issues, "open", config.openSamplerData, config.openSoundClips);
            bool closePlays = CheckEvent(issues, "close", config.closeSamplerData, config.closeSoundClips);
            CheckEvent(issues, "lock", config.lockSamplerData, config.lockSoundClips);
            CheckEvent(issues, "unlock", config.unlockSamplerData, config.unlockSoundClips);

            // The classic half-authored config: the door announces itself on open and then goes
            // quiet, which reads as a bug in playmode rather than as a missing asset.
            if (openPlays && !closePlays)
            {
                issues.Add(new Issue(false,
                    "an open sound is authored but no playable close sound is - the door will sound " +
                    "when opening and stay silent when closing."));
            }
            else if (closePlays && !openPlays)
            {
                issues.Add(new Issue(false,
                    "a close sound is authored but no playable open sound is - the door will sound " +
                    "when closing and stay silent when opening."));
            }

            if (!openPlays && !closePlays)
            {
                issues.Add(new Issue(false,
                    "neither open nor close has a playable sound - a door using this config is silent. " +
                    "Leave doorAudioConfig unassigned instead if that is intended."));
            }

            return issues;
        }

        /// <summary>
        /// Validates one event's two lists and reports whether the event can actually produce
        /// sound at runtime (some sampler entry with a clip, or some legacy clip).
        /// </summary>
        private static bool CheckEvent(List<Issue> issues, string eventName,
            SamplerData[] samplerData, AudioClip[] clips)
        {
            bool samplerCanPlay = false;
            bool samplerHasEntries = samplerData != null && samplerData.Length > 0;
            bool anyEntryFallsThrough = false;

            if (samplerHasEntries)
            {
                for (int i = 0; i < samplerData.Length; i++)
                {
                    var data = samplerData[i];

                    if (data == null)
                    {
                        // The bridge picks one entry at random; a null pick falls through to the
                        // legacy clips, so the event randomly alternates between two sounds (or silence).
                        issues.Add(new Issue(true,
                            $"{eventName} SamplerData slot {i} is empty - when the random pick lands on it " +
                            "the event falls back to the legacy clips (or plays nothing)."));
                        anyEntryFallsThrough = true;
                        continue;
                    }

                    var dataIssues = SamplerDataValidation.Validate(data);
                    foreach (var dataIssue in dataIssues)
                    {
                        issues.Add(new Issue(dataIssue.IsError,
                            $"{eventName} SamplerData '{data.name}': {dataIssue.Message}", data));
                    }

                    if (data.audioClip == null)
                    {
                        anyEntryFallsThrough = true;
                    }
                    else if (!dataIssues.Exists(d => d.IsError))
                    {
                        samplerCanPlay = true;
                    }
                }
            }

            bool clipsCanPlay = false;

            if (clips != null && clips.Length > 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] == null)
                    {
                        issues.Add(new Issue(true,
                            $"{eventName} sound clip slot {i} is empty - when the random pick lands on it " +
                            "the event plays nothing."));
                    }
                    else
                    {
                        clipsCanPlay = true;
                    }
                }
            }

            // "Can play" means some runtime outcome produces sound: a healthy sampler entry, the
            // clip list when no sampler entries are authored, or the clip list reached through a
            // sampler entry that falls through (null entry / entry without a clip).
            return samplerCanPlay
                   || (!samplerHasEntries && clipsCanPlay)
                   || (anyEntryFallsThrough && clipsCanPlay);
        }
    }
}
