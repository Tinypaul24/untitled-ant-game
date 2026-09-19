// What a worker is for.
//
// Three, because there are exactly three sources of work on the board: chambers to excavate, rooms
// to furnish, and food to fetch. Nurse and Soldier are in the README and are not here, because
// there is no larva-tending job and no combat - a role with nothing to do is a slider that does
// nothing, and the colony would be no more calculated for having it.
//
// A role is a priority, not a demarcation. See AntWorker.GoIdle: a worker tries her own trade
// first and falls through to the rest before she will stand about. That is enough for allocation
// to matter, because digging is effectively unlimited work - a colony of all diggers never gets
// round to eating - without producing the absurdity of five idle ants beside an unfetched seed.
public enum AntRole
{
    Digger,
    Builder,
    Forager,
}
