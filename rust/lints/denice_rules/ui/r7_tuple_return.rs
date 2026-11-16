// R7: no tuple returns; the unit type is not a tuple return.
struct Pair {
    left: i32,
    right: i32,
}

fn tuple() -> (i32, i32) {
    (1, 2)
}

fn named() -> Pair {
    Pair { left: 1, right: 2 }
}

fn unit() {}

struct Holder;

impl Holder {
    fn tuple(&self) -> (u8, u8) {
        (0, 0)
    }
}

fn main() {
    let _ = tuple();
    let Pair { left, right } = named();
    let _ = left + right;
    unit();
    let _ = Holder.tuple();
}
