﻿using System;
using DynamicData.Tests.Domain;
using FluentAssertions;
using Xunit;

namespace DynamicData.Tests
{
    public class EnumerableExFixtures
    {
        private readonly Person _person1 = new Person("One", 1);
        private readonly Person _person2 = new Person("Two", 2);
        private readonly Person _person3 = new Person("Three", 3);

        [Fact]
        public void CanConvertToObservableChangeSetList()
        {
            var source = new[] {_person1, _person2, _person3};
            var changeSet = source.AsObservableChangeSet(x => x.Age)
                .AsObservableCache();
            changeSet.Items.ShouldBeEquivalentTo(source);
        }

        [Fact]
        public void CanConvertToObservableChangeSetCache()
        {
            var source = new[] {_person1, _person2, _person3};
            var changeSet = source.AsObservableChangeSet()
                .AsObservableList();
            changeSet.Items.ShouldBeEquivalentTo(source);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RespectsCompleteConfigurationForList(bool shouldComplete)
        {
            var completed = false;
            var source = new[] {_person1, _person2, _person3};
            using (source.AsObservableChangeSet(shouldComplete)
                .Subscribe(_ => { }, () => completed = true))
            {
                Assert.Equal(completed, shouldComplete);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RespectsCompleteConfigurationForCache(bool shouldComplete)
        {
            var completed = false;
            var source = new[] {_person1, _person2, _person3};
            using (source.AsObservableChangeSet(x => x.Age, shouldComplete)
                .Subscribe(_ => { }, () => completed = true))
            {
                Assert.Equal(completed, shouldComplete);
            }
        }
    }
}