using jeanf.audiosystems;
using NUnit.Framework;
using UnityEngine;

namespace AutomaticDoorSystem.Tests
{
    /// <summary>
    /// Locks the AudioSystems integration contract on <see cref="DoorAudioConfiguration"/>:
    /// SamplerData is the preferred source per event, an event with no SamplerData falls back to
    /// the legacy clip lists, and the two never bleed into each other's events. DoorAudioBridge
    /// branches on exactly this — get it wrong and doors go silent or play the wrong asset.
    /// </summary>
    public class DoorSamplerAudioTests
    {
        private DoorAudioConfiguration _config;
        private SamplerData _openData;
        private AudioClip _closeClip;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<DoorAudioConfiguration>();
            _openData = ScriptableObject.CreateInstance<SamplerData>();
            _closeClip = AudioClip.Create("close", 44100, 1, 44100, false);

            _config.openSamplerData = new[] { _openData };
            _config.closeSoundClips = new[] { _closeClip };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_openData);
            Object.DestroyImmediate(_closeClip);
        }

        [Test]
        public void EventWithSamplerData_ReturnsIt()
        {
            Assert.That(_config.GetSamplerDataForEventType(AudioEventType.Open), Is.SameAs(_openData));
        }

        [Test]
        public void EventWithoutSamplerData_ReturnsNull_SoBridgeFallsBackToClips()
        {
            Assert.That(_config.GetSamplerDataForEventType(AudioEventType.Close), Is.Null);
            Assert.That(_config.GetClipForEventType(AudioEventType.Close), Is.SameAs(_closeClip));
        }

        [Test]
        public void EmptyAndNullLists_ReturnNull()
        {
            _config.lockSamplerData = new SamplerData[0];
            Assert.That(_config.GetSamplerDataForEventType(AudioEventType.Lock), Is.Null);
            Assert.That(_config.GetSamplerDataForEventType(AudioEventType.Unlock), Is.Null);
        }

        [Test]
        public void SamplerDataDoesNotLeakAcrossEventTypes()
        {
            Assert.That(_config.GetSamplerDataForEventType(AudioEventType.Close), Is.Null,
                "open SamplerData must not answer close events");
            Assert.That(_config.GetClipForEventType(AudioEventType.Open), Is.Null,
                "legacy clips were not authored for open — the sampler path owns that event");
        }
    }
}
