export const sessionChannels = {
  list: 'desktop-sessions:list',
  create: 'desktop-sessions:create',
  update: 'desktop-sessions:update',
  remove: 'desktop-sessions:remove',
  removeForAgent: 'desktop-sessions:remove-for-agent',
  bulkUpsert: 'desktop-sessions:bulk-upsert',
  getSelectedId: 'desktop-sessions:get-selected-id',
  setSelectedId: 'desktop-sessions:set-selected-id',
} as const
