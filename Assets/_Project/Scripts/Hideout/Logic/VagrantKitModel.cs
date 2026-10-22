using System;
using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.Hideout
{
    // SYS-HUB 부랑자 키트 (§9 "녹슨 권총+탄창 2개+붕대, 20분 쿨다운 무료", M5 설계 §3.3, Q13~Q17). 순수 C#.
    // - 지급 조건은 쿨다운만 (TODO(GDD-?) Q13): 새 프로필은 바로 받는다.
    // - 시계는 실제 시간 UTC (Q14). 남은 시간 = clamp(cooldown − (now − last), 0, cooldown) → 시계를 되돌려도 최대 쿨다운.
    // - 보관함에 전부 들어갈 때만 지급 (Q15). 실패 시 아무것도 놓지 않고 쿨다운도 쓰지 않는다.
    public sealed class VagrantKitModel
    {
        private readonly PlayerProfile profile;
        private readonly HideoutConfigSO config;
        private readonly Func<DateTime> utcNow;

        public VagrantKitModel(PlayerProfile profile, HideoutConfigSO config, Func<DateTime> utcNow)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public float CooldownSec => Math.Max(0f, config.vagrantKitCooldownSec);

        public float RemainingSec
        {
            get
            {
                float cooldown = CooldownSec;
                if (profile.LastKitClaimUtc == null || cooldown <= 0f)
                    return 0f;
                double elapsed = (utcNow() - profile.LastKitClaimUtc.Value).TotalSeconds;
                double remaining = cooldown - elapsed;
                if (remaining <= 0)
                    return 0f;
                return remaining >= cooldown ? cooldown : (float)remaining;
            }
        }

        public bool IsAvailable => RemainingSec <= 0f;

        // 쿨다운 중 → OnCooldown, 지급 목록이 비었으면 Unavailable(설정 오류), 자리 부족 → NoSpace.
        // 성공 시 LastKitClaimUtc = now.
        public KitClaimResult TryClaim()
        {
            if (!IsAvailable)
                return KitClaimResult.OnCooldown;

            var items = new List<ItemInstance>();
            if (config.vagrantKitItems != null)
            {
                foreach (ItemDefinitionSO definition in config.vagrantKitItems)
                    if (definition != null)
                        items.Add(ItemFactory.Create(definition));   // 권총은 탄창 없이, 탄창은 가득 (Q17)
            }
            if (items.Count == 0)
                return KitClaimResult.Unavailable;

            if (!StashRules.TryPlaceAll(profile.Stash, items))
                return KitClaimResult.NoSpace;
            profile.LastKitClaimUtc = utcNow();
            return KitClaimResult.Ok;
        }
    }
}
