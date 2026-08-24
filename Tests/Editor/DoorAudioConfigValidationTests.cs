using System.Collections.Generic;
using System.Linq;
using jeanf.audiosystems;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Tests
{
    /// <summary>
    /// Locks the edit-time validation contract for door audio: DoorAudioConfigurationValidator
    /// must flag every authoring state that plays wrong at runtime (half-authored open/close
    /// pairs, empty random-pick slots, SamplerData loop windows that pin the playhead) and stay
    /// quiet on healthy assets. The project sweep tests at the bottom then hold every serialized
    /// config in this project to that standard - no playmode needed to find a broken one.
    /// </summary>
    public class DoorAudioConfigValidationTests
    {
        private readonly List<Object> _cleanup = new();

        private T Track<T>(T obj) where T : Object
        {
            _cleanup.Add(obj);
            return obj;
        }

        private DoorAudioConfiguration NewConfig() => Track(ScriptableObject.CreateInstance<DoorAudioConfiguration>());

        private AudioClip NewClip(string name) => Track(AudioClip.Create(name, 44100, 1, 44100, false));

        private SamplerData NewSamplerData(string name, AudioClip clip, bool oneShot = true, float loopFrom = 0f, float loopTo = 0f)
        {
            var data = Track(ScriptableObject.CreateInstance<SamplerData>());
            data.name = name;
            data.audioClip = clip;
            data.volume = 1f;
            data.isPlayOneShot = oneShot;
            data.loopFrom = loopFrom;
            data.loopTo = loopTo;
            if (!oneShot) data.playOut = loopTo;
            return data;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _cleanup)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _cleanup.Clear();
        }

        private static List<DoorAudioConfigurationValidator.Issue> Errors(List<DoorAudioConfigurationValidator.Issue> issues)
            => issues.Where(i => i.IsError).ToList();

        [Test]
        public void FullyAuthoredConfig_HasNoIssues()
        {
            var config = NewConfig();
            config.openSoundClips = new[] { NewClip("open") };
            config.closeSoundClips = new[] { NewClip("close") };

            Assert.IsEmpty(DoorAudioConfigurationValidator.Validate(config));
        }

        [Test]
        public void OpenAuthoredButCloseEmpty_FlagsTheAsymmetry()
        {
            var config = NewConfig();
            config.openSoundClips = new[] { NewClip("open") };

            var issues = DoorAudioConfigurationValidator.Validate(config);

            Assert.That(issues.Any(i => i.Message.Contains("stay silent when closing")),
                "a config that only sounds on open must be flagged - this is the 'plays on open but not on close' bug class");
        }

        [Test]
        public void NullClipSlot_IsAnError()
        {
            var config = NewConfig();
            config.openSoundClips = new[] { NewClip("open"), null };
            config.closeSoundClips = new[] { NewClip("close") };

            var issues = DoorAudioConfigurationValidator.Validate(config);

            Assert.That(Errors(issues).Any(i => i.Message.Contains("open sound clip slot 1")),
                "an empty slot in a random-pick list plays nothing when picked and must be an error");
        }

        [Test]
        public void NullSamplerSlot_IsAnError()
        {
            var config = NewConfig();
            config.openSamplerData = new SamplerData[] { null };
            config.closeSoundClips = new[] { NewClip("close") };

            var issues = DoorAudioConfigurationValidator.Validate(config);

            Assert.That(Errors(issues).Any(i => i.Message.Contains("open SamplerData slot 0")));
        }

        [Test]
        public void LoopingSamplerDataWithEmptyWindow_IsAnError()
        {
            var config = NewConfig();
            config.openSamplerData = new[]
            {
                NewSamplerData("openLoop", NewClip("open"), oneShot: false, loopFrom: 0f, loopTo: 0f)
            };
            config.closeSoundClips = new[] { NewClip("close") };

            var issues = DoorAudioConfigurationValidator.Validate(config);

            Assert.That(Errors(issues).Any(i => i.Message.Contains("loop window is empty")),
                "an empty loop window pins the playhead to loopFrom forever - the clip never audibly plays " +
                "and the AudioSource never stops, so this must be an error");
        }

        [Test]
        public void SamplerDataWithoutClip_FallsBackToClips_SoEventStillCounts()
        {
            var config = NewConfig();
            config.openSamplerData = new[] { NewSamplerData("openNoClip", null) };
            config.openSoundClips = new[] { NewClip("open") };
            config.closeSoundClips = new[] { NewClip("close") };

            var issues = DoorAudioConfigurationValidator.Validate(config);

            Assert.That(issues.Any(i => i.IsError), "the clipless SamplerData itself is an error");
            Assert.That(issues.All(i => !i.Message.Contains("stay silent when closing") &&
                                        !i.Message.Contains("stay silent when opening")),
                "the event still reaches the legacy clip fallback, so no asymmetry warning applies");
        }

        [Test]
        public void SilentConfig_IsFlagged()
        {
            var issues = DoorAudioConfigurationValidator.Validate(NewConfig());

            Assert.That(issues.Any(i => i.Message.Contains("neither open nor close")));
        }

        [Test]
        public void ZeroVolumeSamplerData_IsFlagged()
        {
            var data = NewSamplerData("quiet", NewClip("clip"));
            data.volume = 0f;

            var issues = SamplerDataValidation.Validate(data);

            Assert.That(issues.Any(i => !i.IsError && i.Message.Contains("volume is 0")),
                "volume defaults to 0 on a fresh SamplerData asset, so this trap needs a warning");
        }

        // ---- project sweeps: every serialized asset in this project must hold the line ----

        [Test]
        public void EveryDoorAudioConfigurationAssetInProject_HasNoErrors()
        {
            var failures = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:DoorAudioConfiguration"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<DoorAudioConfiguration>(path);
                if (config == null) continue;

                foreach (var issue in DoorAudioConfigurationValidator.Validate(config).Where(i => i.IsError))
                {
                    failures.Add($"{path}: {issue.Message}");
                }
            }

            Assert.IsEmpty(failures,
                "DoorAudioConfiguration assets with errors (doors using them will misplay in playmode):\n  " +
                string.Join("\n  ", failures));
        }

        [Test]
        public void EverySamplerDataAssetInProject_HasNoErrors()
        {
            var failures = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:SamplerData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<SamplerData>(path);
                if (data == null) continue;

                foreach (var issue in SamplerDataValidation.Validate(data).Where(i => i.IsError))
                {
                    failures.Add($"{path}: {issue.Message}");
                }
            }

            Assert.IsEmpty(failures,
                "SamplerData assets with errors (they play wrong or wedge the AudioSource at runtime):\n  " +
                string.Join("\n  ", failures));
        }
    }
}
