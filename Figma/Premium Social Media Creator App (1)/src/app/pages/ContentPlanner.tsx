import { useMemo, useState } from "react";
import { Calendar as CalendarIcon, ChevronLeft, ChevronRight, Clock } from "lucide-react";
import { useStudio } from "../state/studioStore";

const daysOfWeek = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

function formatDateTime(value?: string) {
  if (!value) {
    return "Not scheduled";
  }

  return new Date(value).toLocaleString([], {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function toDateKey(date: Date) {
  return date.toISOString().split("T")[0];
}

export function ContentPlanner() {
  const {
    state: { drafts, selectedDraftId },
    updateDraft,
    selectDraft,
  } = useStudio();

  const [currentDate, setCurrentDate] = useState(new Date());
  const [selectedDate, setSelectedDate] = useState<Date>(new Date());
  const [scheduleValue, setScheduleValue] = useState("");

  const schedulableDrafts = useMemo(() => drafts.filter((draft) => draft.status !== "published-manual"), [drafts]);
  const scheduledDrafts = useMemo(
    () => drafts.filter((draft) => draft.scheduledFor).sort((left, right) => String(left.scheduledFor).localeCompare(String(right.scheduledFor))),
    [drafts],
  );

  const upcomingDrafts = useMemo(() => {
    const now = Date.now();
    const nextWeek = now + 1000 * 60 * 60 * 24 * 7;
    return scheduledDrafts.filter((draft) => {
      const value = new Date(String(draft.scheduledFor)).getTime();
      return value >= now && value <= nextWeek;
    });
  }, [scheduledDrafts]);

  const selectedDraft = schedulableDrafts.find((draft) => draft.id === selectedDraftId) ?? schedulableDrafts[0];
  const selectedDateKey = toDateKey(selectedDate);
  const selectedDateAgenda = scheduledDrafts.filter((draft) => String(draft.scheduledFor).startsWith(selectedDateKey));

  const days = useMemo(() => {
    const year = currentDate.getFullYear();
    const month = currentDate.getMonth();
    const firstDay = new Date(year, month, 1);
    const lastDay = new Date(year, month + 1, 0);
    const result: Array<Date | null> = [];

    for (let index = 0; index < firstDay.getDay(); index += 1) {
      result.push(null);
    }

    for (let day = 1; day <= lastDay.getDate(); day += 1) {
      result.push(new Date(year, month, day));
    }

    return result;
  }, [currentDate]);

  function navigateMonth(direction: number) {
    setCurrentDate(new Date(currentDate.getFullYear(), currentDate.getMonth() + direction, 1));
  }

  function saveSchedule() {
    if (!selectedDraft || !scheduleValue) {
      return;
    }

    updateDraft(selectedDraft.id, (draft) => ({
      ...draft,
      scheduledFor: new Date(scheduleValue).toISOString(),
      status: "scheduled",
    }));
  }

  function clearSchedule(draftId: string) {
    updateDraft(draftId, (draft) => ({
      ...draft,
      scheduledFor: undefined,
      status: "draft",
    }));
  }

  const monthName = currentDate.toLocaleString("en-US", { month: "long", year: "numeric" });

  return (
    <div className="h-full overflow-y-auto bg-[#0a0a0a]">
      <div className="p-8 space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">Content Planner</h1>
          <p className="text-gray-400">
            The planner only shows schedules attached to real drafts. Timing suggestions, auto-post queues, and optimization should come from connected analytics or publishing services later.
          </p>
        </div>

        <div className="grid grid-cols-4 gap-4">
          <div className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
            <div className="text-2xl font-bold text-white mb-1">{scheduledDrafts.length}</div>
            <div className="text-sm text-gray-400">Scheduled drafts</div>
          </div>
          <div className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
            <div className="text-2xl font-bold text-white mb-1">{schedulableDrafts.length}</div>
            <div className="text-sm text-gray-400">Schedulable drafts</div>
          </div>
          <div className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
            <div className="text-2xl font-bold text-white mb-1">{upcomingDrafts.length}</div>
            <div className="text-sm text-gray-400">Next 7 days</div>
          </div>
          <div className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
            <div className="text-2xl font-bold text-white mb-1">{drafts.filter((draft) => draft.status === "published-manual").length}</div>
            <div className="text-sm text-gray-400">Manual publish records</div>
          </div>
        </div>

        <div className="grid grid-cols-[1fr_360px] gap-6">
          <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-3">
                <button onClick={() => navigateMonth(-1)} className="w-9 h-9 rounded-lg bg-white/5 hover:bg-white/10 flex items-center justify-center transition-colors">
                  <ChevronLeft className="w-5 h-5 text-gray-400" />
                </button>
                <h2 className="text-xl font-semibold text-white">{monthName}</h2>
                <button onClick={() => navigateMonth(1)} className="w-9 h-9 rounded-lg bg-white/5 hover:bg-white/10 flex items-center justify-center transition-colors">
                  <ChevronRight className="w-5 h-5 text-gray-400" />
                </button>
              </div>
              <div className="text-sm text-gray-400">Select a day to inspect scheduled drafts</div>
            </div>

            <div className="grid grid-cols-7 gap-2">
              {daysOfWeek.map((day) => (
                <div key={day} className="text-center text-sm font-medium text-gray-400 pb-2">
                  {day}
                </div>
              ))}

              {days.map((date, index) => {
                const dateKey = date ? toDateKey(date) : undefined;
                const content = dateKey ? scheduledDrafts.filter((draft) => String(draft.scheduledFor).startsWith(dateKey)) : [];
                const isToday = date?.toDateString() === new Date().toDateString();
                const isSelected = date?.toDateString() === selectedDate.toDateString();

                return (
                  <button
                    key={index}
                    onClick={() => date && setSelectedDate(date)}
                    className={`min-h-[122px] rounded-lg p-2 border text-left transition-all ${
                      date
                        ? isSelected
                          ? "bg-cyan-500/12 border-cyan-400/40"
                          : isToday
                            ? "bg-violet-500/10 border-violet-500/40"
                            : "bg-[#0f0f0f] border-white/5 hover:border-white/20"
                        : "bg-transparent border-transparent"
                    }`}
                  >
                    {date && (
                      <>
                        <div className={`text-sm font-medium mb-2 ${isSelected ? "text-cyan-300" : isToday ? "text-violet-400" : "text-gray-300"}`}>
                          {date.getDate()}
                        </div>
                        <div className="space-y-1">
                          {content.slice(0, 3).map((item) => (
                            <div key={item.id} className="bg-gradient-to-r from-cyan-500/25 to-blue-600/25 p-1.5 rounded text-xs text-white border border-cyan-400/20">
                              <div className="line-clamp-1 font-medium">{item.title}</div>
                              <div className="text-[10px] text-cyan-100/80 mt-0.5">{formatDateTime(item.scheduledFor)}</div>
                            </div>
                          ))}
                        </div>
                      </>
                    )}
                  </button>
                );
              })}
            </div>
          </section>

          <div className="space-y-6">
            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
              <h3 className="text-lg font-semibold text-white mb-4">Schedule A Draft</h3>
              {schedulableDrafts.length === 0 ? (
                <div className="text-sm text-gray-400">Create and save a draft first, then attach a real schedule here.</div>
              ) : (
                <div className="space-y-3">
                  <select
                    value={selectedDraft?.id ?? ""}
                    onChange={(event) => selectDraft(event.target.value)}
                    className="w-full bg-[#0f0f0f] border border-white/10 rounded-lg px-4 py-2.5 text-white"
                  >
                    {schedulableDrafts.map((draft) => (
                      <option key={draft.id} value={draft.id}>
                        {draft.title}
                      </option>
                    ))}
                  </select>
                  <input
                    type="datetime-local"
                    value={scheduleValue}
                    onChange={(event) => setScheduleValue(event.target.value)}
                    className="w-full bg-[#0f0f0f] border border-white/10 rounded-lg px-4 py-2.5 text-white"
                  />
                  <button
                    onClick={saveSchedule}
                    className="w-full bg-gradient-to-r from-violet-500 to-fuchsia-500 text-white px-4 py-3 rounded-lg text-sm font-medium"
                  >
                    Save Schedule
                  </button>
                </div>
              )}
            </section>

            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
              <div className="flex items-center gap-2 mb-4">
                <CalendarIcon className="w-5 h-5 text-cyan-300" />
                <h3 className="text-lg font-semibold text-white">Agenda For {selectedDate.toLocaleDateString([], { month: "short", day: "numeric" })}</h3>
              </div>
              {selectedDateAgenda.length === 0 ? (
                <div className="text-sm text-gray-400">No drafts are scheduled for this day.</div>
              ) : (
                <div className="space-y-3">
                  {selectedDateAgenda.map((draft) => (
                    <div key={draft.id} className="bg-[#0f0f0f] border border-white/10 rounded-lg p-4">
                      <div className="text-sm font-medium text-white mb-1">{draft.title}</div>
                      <div className="text-xs text-gray-400 mb-3">{formatDateTime(draft.scheduledFor)} · {draft.linkedPlatformIds.join(", ") || "No platform"}</div>
                      <button
                        onClick={() => clearSchedule(draft.id)}
                        className="text-xs text-violet-400 hover:text-violet-300"
                      >
                        Remove schedule
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </section>

            <section className="bg-gradient-to-br from-violet-500/10 to-fuchsia-500/10 border border-violet-500/30 rounded-xl p-6">
              <div className="flex items-center gap-2 mb-3 text-white font-medium">
                <Clock className="w-4 h-4 text-violet-400" />
                Planning Boundary
              </div>
              <div className="text-sm text-gray-300 leading-relaxed">
                This planner intentionally avoids fake best-time suggestions or fake queue metrics. When Atlas has real analytics or publishing integrations,
                that logic can plug into the same schedule model already used here.
              </div>
            </section>
          </div>
        </div>
      </div>
    </div>
  );
}
