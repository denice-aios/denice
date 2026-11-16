// R4: processes and environment variables only in their owner modules.
fn main() {
    let _ = std::process::Command::new("ls");
    let _ = std::env::var("HOME");
    let _ = std::env::var_os("HOME");
}
