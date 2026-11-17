// R5: no environment-dependent folders; the program's own path only where allowed.
fn main() {
    let _ = std::env::temp_dir();
    let _ = std::env::current_dir();
    let _ = std::env::current_exe();
}
