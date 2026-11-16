// R3: an allow-listed platform module may branch on the OS.
#[cfg(target_os = "linux")]
const NAME: &str = "linux";
#[cfg(not(target_os = "linux"))]
const NAME: &str = "other";

fn main() {
    let _ = NAME;
    let _ = cfg!(unix);
}
