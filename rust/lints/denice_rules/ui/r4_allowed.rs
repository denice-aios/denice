// R4: the owner module may start processes and read environment variables.
fn main() {
    let _ = std::process::Command::new("ls");
    let _ = std::env::var("HOME");
}
