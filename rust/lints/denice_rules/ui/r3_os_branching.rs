// R3: branching on the OS or architecture outside the allow-listed platform modules is reported.
#[cfg(windows)]
fn suffix() -> &'static str {
    ".exe"
}

#[cfg(not(windows))]
fn suffix() -> &'static str {
    ""
}

#[cfg(test)]
mod tests {}

fn main() {
    let _ = suffix();
    let _ = cfg!(target_os = "macos");
    let _ = cfg!(debug_assertions);
}
