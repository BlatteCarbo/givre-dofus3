use std::path::PathBuf;

pub const LEASE_FILE: &str = "tactical-map.lease";
pub const ACK_FILE: &str = "tactical-map.restored";
pub const CONSTRUCTION_FILE: &str = "tactical-map.construction";
pub const MAX_TTL_MS: u64 = 3000;

// Capture admission only. Deliberately not convertible to Lease: this record
// cannot authorize managed traversal, cleanup, or activation off-map.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Construction {
    pub target_start: u64,
    pub owner_pid: u32,
    pub owner_start: u64,
    pub epoch: u64,
    pub map_id: i64,
    pub desired_generation: u64,
    pub expires: u64,
}

impl Construction {
    pub fn parse(text: &str, now: u64, target_start: u64) -> Option<Self> {
        if text.len() > 256 || target_start == 0 { return None; }
        let mut fields = text.split_whitespace();
        let mut values = [0_u64; 8];
        for value in &mut values { *value = fields.next()?.parse().ok()?; }
        if fields.next().is_some() { return None; }
        let [version, target, owner, owner_start, epoch, map, generation, expires] = values;
        if version != 1 || target != target_start || owner == 0 || owner_start == 0
            || epoch == 0 || map == 0 || map > i32::MAX as u64 || generation == 0
            || expires <= now || expires - now > MAX_TTL_MS { return None; }
        Some(Self {
            target_start: target, owner_pid: u32::try_from(owner).ok()?, owner_start,
            epoch, map_id: i64::try_from(map).ok()?, desired_generation: generation, expires,
        })
    }
}

pub fn read_construction_at(directory: &std::path::Path, target_start: u64, now: u64) -> Option<Construction> {
    use std::io::Read;
    let mut text = String::new();
    std::fs::File::open(directory.join(CONSTRUCTION_FILE)).ok()?.take(257).read_to_string(&mut text).ok()?;
    let construction = Construction::parse(&text, now, target_start)?;
    (process_start(construction.owner_pid) == Some(construction.owner_start)).then_some(construction)
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Lease {
    pub target_start: u64,
    pub owner_pid: u32,
    pub owner_start: u64,
    pub epoch: u64,
    pub map_id: i64,
    pub generation: u64,
    pub enabled: bool,
    pub expires: u64,
}

impl Lease {
    pub fn parse(text: &str, now: u64, target_start: u64) -> Option<Self> {
        if text.len() > 256 || target_start == 0 {
            return None;
        }
        let values = text.split_whitespace()
            .map(str::parse::<u64>).collect::<Result<Vec<_>, _>>().ok()?;
        let [1, target, owner, owner_start, epoch, map, generation, enabled, expires] =
            values.as_slice() else { return None };
        if *target != target_start || *owner == 0 || *owner > u32::MAX as u64
            || *owner_start == 0 || *epoch == 0 || *map == 0 || *map > i32::MAX as u64
            || *generation == 0 || *enabled > 1 || *expires <= now
            || *expires - now > MAX_TTL_MS
        {
            return None;
        }
        Some(Self {
            target_start: *target, owner_pid: *owner as u32, owner_start: *owner_start,
            epoch: *epoch, map_id: *map as i64, generation: *generation,
            enabled: *enabled != 0, expires: *expires,
        })
    }

    pub fn token(self) -> String {
        format!("1 {} {} {} {} {} {} {}", self.target_start, self.owner_pid,
            self.owner_start, self.epoch, self.map_id, self.generation, u8::from(self.enabled))
    }

    pub fn same_map(self, other: Self) -> bool {
        self.target_start == other.target_start && self.owner_pid == other.owner_pid
            && self.owner_start == other.owner_start && self.epoch == other.epoch
            && self.map_id == other.map_id
    }
}

#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetModuleHandleExW(flags: u32, address: *const u16, module: *mut usize) -> i32;
    fn GetModuleFileNameW(module: usize, path: *mut u16, size: u32) -> u32;
    fn OpenProcess(access: u32, inherit: i32, pid: u32) -> usize;
    fn GetProcessTimes(handle: usize, created: *mut u64, exited: *mut u64,
        kernel: *mut u64, user: *mut u64) -> i32;
    fn GetExitCodeProcess(handle: usize, code: *mut u32) -> i32;
    fn CloseHandle(handle: usize) -> i32;
    pub fn GetTickCount64() -> u64;
}

pub fn process_start(pid: u32) -> Option<u64> {
    unsafe {
        let handle = OpenProcess(0x1000, 0, pid);
        if handle == 0 { return None; }
        let (mut created, mut exited, mut kernel, mut user, mut code) = (0, 0, 0, 0, 0);
        let valid = GetProcessTimes(handle, &mut created, &mut exited, &mut kernel, &mut user) != 0
            && GetExitCodeProcess(handle, &mut code) != 0 && code == 259;
        CloseHandle(handle);
        valid.then_some(created)
    }
}

pub fn directory() -> Option<PathBuf> {
    use std::os::windows::ffi::OsStringExt;
    let mut module = 0;
    let mut path = [0_u16; 32768];
    unsafe {
        if GetModuleHandleExW(6, directory as *const () as *const u16, &mut module) == 0 {
            return None;
        }
        let count = GetModuleFileNameW(module, path.as_mut_ptr(), path.len() as u32) as usize;
        if count == 0 || count >= path.len() { return None; }
        PathBuf::from(std::ffi::OsString::from_wide(&path[..count])).parent().map(PathBuf::from)
    }
}

pub fn read(directory: &std::path::Path, target_start: u64) -> Option<Lease> {
    read_at(directory, target_start, unsafe { GetTickCount64() })
}

fn read_at(directory: &std::path::Path, target_start: u64, now: u64) -> Option<Lease> {
    use std::io::Read;
    let mut text = String::new();
    std::fs::File::open(directory.join(LEASE_FILE)).ok()?.take(257).read_to_string(&mut text).ok()?;
    let lease = Lease::parse(&text, now, target_start)?;
    (process_start(lease.owner_pid) == Some(lease.owner_start)).then_some(lease)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn construction_is_bounded_and_never_a_ready_lease() {
        let text = "1 100 42 200 3 1234 9 3250";
        let pending = Construction::parse(text, 1000, 100).unwrap();
        assert_eq!(pending.epoch, 3);
        assert_eq!(pending.desired_generation, 9);
        assert!(Lease::parse(text, 1000, 100).is_none());
        assert!(Construction::parse("1 100 42 200 3 1234 9 1 3250", 1000, 100).is_none());
        assert!(Construction::parse(text, 3250, 100).is_none());
        assert!(Construction::parse(text, 249, 100).is_none());
        assert!(Construction::parse(text, 250, 100).is_some());
        assert!(Construction::parse(text, 1000, 101).is_none());
        for bad in ["2 100 42 200 3 1234 9 3250", "1 100 0 200 3 1234 9 3250",
            "1 100 4294967296 200 3 1234 9 3250", "1 100 42 0 3 1234 9 3250",
            "1 100 42 200 0 1234 9 3250", "1 100 42 200 3 2147483648 9 3250",
            "1 100 42 200 3 1234 0 3250", "1 100 42 200 3 1234 9", ""] {
            assert!(Construction::parse(bad, 1000, 100).is_none(), "{bad}");
        }
    }

    #[test]
    fn construction_file_requires_current_owner_and_is_independent_of_ready_file() {
        let pid = std::process::id();
        let start = process_start(pid).unwrap();
        let directory = std::env::temp_dir().join(format!("tactical-construction-{pid}"));
        std::fs::create_dir(&directory).unwrap();
        let path = directory.join(CONSTRUCTION_FILE);
        std::fs::write(&path, format!("1 {start} {pid} {start} 3 1234 9 3250")).unwrap();
        assert!(read_construction_at(&directory, start, 1000).is_some());
        assert!(read_at(&directory, start, 1000).is_none());
        std::fs::write(&path, format!("1 {start} {pid} {} 3 1234 9 3250", start + 1)).unwrap();
        assert!(read_construction_at(&directory, start, 1000).is_none());
        std::fs::remove_file(&path).unwrap();
        assert!(read_construction_at(&directory, start, 1000).is_none());
        std::fs::remove_dir(&directory).unwrap();
    }

    #[test]
    fn requires_fresh_owned_map_identity() {
        let valid = "1 100 42 200 3 1234 9 1 4000";
        let lease = Lease::parse(valid, 1000, 100).unwrap();
        assert!(lease.enabled);
        assert_eq!(lease.token(), "1 100 42 200 3 1234 9 1");
        assert!(Lease::parse(valid, 4000, 100).is_none());
        assert!(Lease::parse(valid, 999, 100).is_none());
        assert!(Lease::parse(valid, 1000, 101).is_none());
        assert!(Lease::parse(valid, 1000, 0).is_none());
    }

    #[test]
    fn rejects_partial_unknown_and_unbounded_records() {
        for invalid in [
            "", "1 100", "2 100 42 200 3 1234 9 1 4000",
            "1 100 0 200 3 1234 9 1 4000", "1 100 42 0 3 1234 9 1 4000",
            "1 100 42 200 0 1234 9 1 4000", "1 100 42 200 3 0 9 1 4000",
            "1 100 42 200 3 2147483648 9 1 4000", "1 100 42 200 3 1234 0 1 4000",
            "1 100 42 200 3 1234 9 2 4000", "1 100 42 200 3 1234 9 1 4000 extra",
        ] {
            assert!(Lease::parse(invalid, 1000, 100).is_none(), "{invalid}");
        }
    }

    #[test]
    fn disabling_preserves_cleanup_permission_not_activation() {
        let active = Lease::parse("1 100 42 200 3 1234 9 1 4000", 1000, 100).unwrap();
        let disabled = Lease::parse("1 100 42 200 3 1234 10 0 4000", 1000, 100).unwrap();
        assert!(!disabled.enabled);
        assert!(active.same_map(disabled));
        assert_ne!(active.token(), disabled.token());
        let next_map = Lease { epoch: 4, ..active };
        assert!(!active.same_map(next_map));
        let next_owner = Lease { owner_start: 201, ..active };
        assert!(!active.same_map(next_owner));
    }

    #[test]
    fn every_ownership_change_requires_a_new_map_binding() {
        // Given a valid active lease and replacements differing in one identity.
        let active = Lease::parse("1 100 42 200 3 1234 9 1 4000", 1000, 100).unwrap();
        for replacement in [
            Lease { target_start: 101, ..active },
            Lease { owner_pid: 43, ..active },
            Lease { owner_start: 201, ..active },
            Lease { epoch: 4, ..active },
            Lease { map_id: 1235, ..active },
        ] {
            // When the tick compares the next ownership scope.
            let same_map = active.same_map(replacement);
            // Then neither direction can reuse the previous binding.
            assert!(!same_map, "{replacement:?}");
            assert!(!replacement.same_map(active), "{replacement:?}");
            assert_ne!(active.token(), replacement.token());
        }
    }

    #[test]
    fn lease_expires_at_the_exact_boundary_even_near_clock_limit() {
        // Given a lease whose expiry is the largest representable clock value.
        let text = format!("1 100 42 200 3 1234 9 1 {}", u64::MAX);
        // When validating its permitted TTL window and exact expiry.
        let at_ttl = Lease::parse(&text, u64::MAX - MAX_TTL_MS, 100);
        let too_early = Lease::parse(&text, u64::MAX - MAX_TTL_MS - 1, 100);
        let last_tick = Lease::parse(&text, u64::MAX - 1, 100);
        let expired = Lease::parse(&text, u64::MAX, 100);
        // Then only live leases within the TTL are usable, without overflow.
        assert!(at_ttl.is_some());
        assert!(last_tick.is_some());
        assert!(too_early.is_none());
        assert!(expired.is_none());
    }

    #[test]
    fn padded_or_overflowing_records_cannot_grant_scene_access() {
        // Given records outside the byte bound or numeric domain.
        let oversized = format!("1 100 42 200 3 1234 9 1 4000{}", " ".repeat(256));
        for invalid in [
            oversized.as_str(),
            "1 100 4294967296 200 3 1234 9 1 4000",
            "1 100 42 200 3 1234 9 1 18446744073709551616",
            "1 100 42 200 3 -1234 9 1 4000",
        ] {
            // When parsing the untrusted lease file.
            let parsed = Lease::parse(invalid, 1000, 100);
            // Then no cleanup or activation permission is returned.
            assert!(parsed.is_none(), "{invalid}");
        }
    }

    #[test]
    fn file_consumer_requires_live_owner_and_revokes_without_delay() {
        let pid = std::process::id();
        let start = process_start(pid).unwrap();
        let now = unsafe { GetTickCount64() };
        let directory = std::env::temp_dir().join(format!("tactical-lease-test-{pid}-{now}"));
        std::fs::create_dir(&directory).unwrap();
        let path = directory.join(LEASE_FILE);
        let record = |owner_start| format!("1 {start} {pid} {owner_start} 3 1234 9 1 3250");
        std::fs::write(&path, record(start)).unwrap();
        assert!(read_at(&directory, start, 1000).is_some());
        std::fs::write(&path, record(start + 1)).unwrap();
        assert!(read_at(&directory, start, 1000).is_none());
        std::fs::write(&path, record(start)).unwrap();
        assert!(read_at(&directory, start + 1, 1000).is_none());
        std::fs::remove_file(&path).unwrap();
        assert!(read_at(&directory, start, 1000).is_none());
        std::fs::remove_dir(&directory).unwrap();
    }
}
