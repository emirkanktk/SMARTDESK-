import { Resource, ResourceType } from '../app/core/models/resource.model';

export const MEETING_ROOM_TYPE: ResourceType = { id: 'type-room', name: 'Meeting Room' };
export const DESK_TYPE: ResourceType = { id: 'type-desk', name: 'Desk' };

export function fakeResource(overrides: Partial<Resource> = {}): Resource {
  return {
    id: 'res-1',
    name: 'Meeting Room A',
    resourceTypeId: MEETING_ROOM_TYPE.id,
    resourceTypeName: MEETING_ROOM_TYPE.name,
    capacity: 8,
    location: '2nd Floor',
    status: 'Active',
    description: null,
    features: ['TV', 'Video Conference'],
    teams: [],
    photoUpdatedAt: null,
    checkInRequired: false,
    position: null,
    ...overrides,
  };
}
